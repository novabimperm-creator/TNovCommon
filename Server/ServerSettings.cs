using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TNovCommon.Server
{
    /// <summary>
    /// Общие настройки TNovApi для всех пользователей: файл {ServerPath}tnovapi.json
    ///   { "ApiUrl": "...", "ApiKey": "...", "ChecklistStorage": "api" | "files" }
    /// накладывается на локальный TNovConfig.json. Непустое значение в локальном конфиге
    /// важнее серверного — так API можно включить (или выключить) одному пользователю.
    ///
    /// Файл на сервере читается только в фоне и не чаще раза в <see cref="RefreshInterval"/>:
    /// конфигурация запрашивается почти каждой командой, и сетевое чтение на UI-потоке
    /// стоило бы удалённому офису сотни миллисекунд. Последние прочитанные настройки
    /// сохраняются локально (%LOCALAPPDATA%\TNov\cache\tnovapi.{хэш ServerPath}.json) и
    /// подхватываются синхронно при первом обращении (только локальный диск) — окна,
    /// открытые до фонового чтения, работают с тем же хранилищем, что и после.
    /// Нет сети — действуют последние прочитанные; файла на сервере нет (папка доступна) —
    /// только локальные настройки, локальная копия удаляется.
    /// </summary>
    public static class ServerSettings
    {
        public const string FileName = "tnovapi.json";
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

        private sealed class Settings
        {
            public string ApiUrl { get; set; }
            public string ApiKey { get; set; }
            public string ChecklistStorage { get; set; }
            public bool? FileSync { get; set; }
            public string MinPluginVersion { get; set; }
            public string UpdateMessage { get; set; }
            public string BlockBelowVersion { get; set; }
            public bool? BlockFilesMode { get; set; }
            public string BlockMessage { get; set; }
        }

        /// <summary>
        /// Серверные настройки изменились после фонового чтения (в т.ч. появились или пропали).
        /// Вызывается в фоновом потоке — подписчик сам переходит в UI-поток.
        /// </summary>
        public static event Action Changed;

        /// <summary>Неизменяемая пара «для какого сервера — какие настройки» (публикуется одной записью).</summary>
        private sealed class Loaded
        {
            public string For;
            public Settings Settings; // null — файла на сервере нет
        }

        private static readonly object _publishLock = new object();
        private static Loaded _loaded;
        private static string _persistTriedFor;    // ServerPath, для которого уже читали локальную копию
        private static string _checkedFor;         // ServerPath последней попытки (в т.ч. неудачной)
        private static DateTime _checkedUtc = DateTime.MinValue;
        private static int _refreshing;

        /// <summary>Наложить серверные настройки на копию локального конфига (без сетевых операций).</summary>
        public static void ApplyTo(TNovConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.ServerPath)) return;

            EnsurePersistedLoaded(config.ServerPath);
            RefreshIfStale(config.ServerPath);

            Loaded loaded = Volatile.Read(ref _loaded);
            if (loaded == null || !SameServer(loaded.For, config.ServerPath)) return;
            Settings s = loaded.Settings;
            if (s == null) return;

            if (string.IsNullOrWhiteSpace(config.ApiUrl)) config.ApiUrl = s.ApiUrl;
            if (string.IsNullOrWhiteSpace(config.ApiKey)) config.ApiKey = s.ApiKey;
            if (string.IsNullOrWhiteSpace(config.ChecklistStorage)) config.ChecklistStorage = s.ChecklistStorage;
            if (config.FileSync == null) config.FileSync = s.FileSync;
            // Требования к версии и блокировку задаёт только сервер: локально их не переопределить.
            config.MinPluginVersion = s.MinPluginVersion;
            config.UpdateMessage = s.UpdateMessage;
            config.BlockBelowVersion = s.BlockBelowVersion;
            config.BlockFilesMode = s.BlockFilesMode == true;
            config.BlockMessage = s.BlockMessage;
            config.ServerChecklistStorage = s.ChecklistStorage;
        }

        /// <summary>Прочитать файл сразу (в фоне) — при старте Revit, чтобы первая команда уже видела настройки.</summary>
        public static void Prime(string serverPath)
        {
            if (string.IsNullOrEmpty(serverPath)) return;
            EnsurePersistedLoaded(serverPath);
            RefreshIfStale(serverPath, force: true);
        }

        private static bool SameServer(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Один раз на ServerPath: локальная копия последних прочитанных настроек (локальный диск, быстро).</summary>
        private static void EnsurePersistedLoaded(string serverPath)
        {
            if (SameServer(Volatile.Read(ref _persistTriedFor), serverPath)) return;
            lock (_publishLock)
            {
                if (SameServer(_persistTriedFor, serverPath)) return;
                _persistTriedFor = serverPath;

                // Фоновое чтение уже успело — оно свежее локальной копии.
                if (_loaded != null && SameServer(_loaded.For, serverPath)) return;

                Settings persisted = ReadPersisted(serverPath);
                if (persisted != null)
                    Volatile.Write(ref _loaded, new Loaded { For = serverPath, Settings = persisted });
            }
        }

        private static void RefreshIfStale(string serverPath, bool force = false)
        {
            // Попытка не чаще раза в интервал, даже неудачная — без сети не долбим шару на каждой команде.
            bool otherServer = !SameServer(Volatile.Read(ref _checkedFor), serverPath);
            if (!force && !otherServer && DateTime.UtcNow - _checkedUtc < RefreshInterval) return;
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;

            _checkedUtc = DateTime.UtcNow;
            Volatile.Write(ref _checkedFor, serverPath);
            Task.Run(() =>
            {
                try
                {
                    string path = ServerData.Combine(serverPath, FileName);
                    if (!File.Exists(path))
                    {
                        // File.Exists = false и при обрыве сети: «файла нет» — только если папка доступна.
                        if (!Directory.Exists(serverPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"Серверная папка недоступна, {FileName} не прочитан");
                            return;
                        }
                        Publish(serverPath, null);
                        DeletePersisted(serverPath);
                        return;
                    }
                    Settings settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path));
                    Publish(serverPath, settings);
                    if (settings != null) Persist(serverPath, settings);
                    else DeletePersisted(serverPath);
                }
                catch (Exception ex)
                {
                    // Сеть или битый JSON: оставляем последние прочитанные настройки.
                    System.Diagnostics.Debug.WriteLine($"Не удалось прочитать {FileName}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }

        private static void Publish(string serverPath, Settings settings)
        {
            bool changed;
            lock (_publishLock)
            {
                Loaded previous = _loaded;
                changed = previous == null || !SameServer(previous.For, serverPath)
                    || JsonConvert.SerializeObject(previous.Settings) != JsonConvert.SerializeObject(settings);
                Volatile.Write(ref _loaded, new Loaded { For = serverPath, Settings = settings });
                _persistTriedFor = serverPath; // локальная копия уже не нужна — есть свежие данные
            }
            if (!changed) return;
            try { Changed?.Invoke(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ошибка обработчика {FileName}: {ex.Message}"); }
        }

        // ---------- локальная копия ----------

        private static string PersistedPathOf(string serverPath)
        {
            string normalized = serverPath.Trim().TrimEnd('\\', '/').Replace('/', '\\').ToLowerInvariant();
            string hash;
            using (var sha = SHA1.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "").Substring(0, 16);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TNov", "cache", "tnovapi." + hash + ".json");
        }

        private static Settings ReadPersisted(string serverPath)
        {
            try
            {
                string path = PersistedPathOf(serverPath);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не удалось прочитать локальную копию {FileName}: {ex.Message}");
                return null;
            }
        }

        private static void Persist(string serverPath, Settings settings)
        {
            try
            {
                string path = PersistedPathOf(serverPath);
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                if (File.Exists(path) && File.ReadAllText(path) == json) return;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Через временный файл: второй экземпляр Revit не прочитает недописанное.
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, json);
                    if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
                    else File.Move(temp, path);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не удалось сохранить локальную копию {FileName}: {ex.Message}");
            }
        }

        private static void DeletePersisted(string serverPath)
        {
            try
            {
                string path = PersistedPathOf(serverPath);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }
    }
}
