using Newtonsoft.Json;
using System;
using System.IO;
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
    /// Файл читается только в фоне и не чаще раза в <see cref="RefreshInterval"/>:
    /// конфигурация запрашивается почти каждой командой, и сетевое чтение на UI-потоке
    /// стоило бы удалённому офису сотни миллисекунд. До первого успешного чтения
    /// (обычно считанные секунды после старта Revit) действуют только локальные настройки.
    /// Нет файла или нет сети — тоже только локальные (последние прочитанные сохраняются).
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
        }

        private static Settings _settings;
        private static string _loadedFor;          // ServerPath, для которого прочитаны настройки
        private static string _checkedFor;         // ServerPath последней попытки (в т.ч. неудачной)
        private static DateTime _checkedUtc = DateTime.MinValue;
        private static int _refreshing;

        /// <summary>Наложить серверные настройки на копию локального конфига (без сетевых операций).</summary>
        public static void ApplyTo(TNovConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.ServerPath)) return;

            RefreshIfStale(config.ServerPath);

            Settings s = Volatile.Read(ref _settings);
            if (s == null || !string.Equals(Volatile.Read(ref _loadedFor), config.ServerPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(config.ApiUrl)) config.ApiUrl = s.ApiUrl;
            if (string.IsNullOrWhiteSpace(config.ApiKey)) config.ApiKey = s.ApiKey;
            if (string.IsNullOrWhiteSpace(config.ChecklistStorage)) config.ChecklistStorage = s.ChecklistStorage;
        }

        /// <summary>Прочитать файл сразу (в фоне) — при старте Revit, чтобы первая команда уже видела настройки.</summary>
        public static void Prime(string serverPath) => RefreshIfStale(serverPath, force: true);

        private static void RefreshIfStale(string serverPath, bool force = false)
        {
            // Попытка не чаще раза в интервал, даже неудачная — без сети не долбим шару на каждой команде.
            bool otherServer = !string.Equals(Volatile.Read(ref _checkedFor), serverPath, StringComparison.OrdinalIgnoreCase);
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
                        Publish(serverPath, null);
                        return;
                    }
                    Publish(serverPath, JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)));
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
            Volatile.Write(ref _settings, settings);
            Volatile.Write(ref _loadedFor, serverPath);
        }
    }
}
