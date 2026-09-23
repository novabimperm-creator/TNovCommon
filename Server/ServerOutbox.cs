using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TNovCommon.Server
{
    /// <summary>
    /// Отложенная дозапись строк в файлы на сервере (usage.txt, users/…, projects/…synchronizes.txt).
    ///
    /// Раньше каждая команда синхронно делала File.AppendAllText по сети — в удалённом
    /// офисе это заметная пауза на каждый запуск. Теперь строка кладётся в очередь,
    /// а фоновый таймер раз в <see cref="FlushInterval"/> дописывает накопленное
    /// одним открытием на файл. Если сервер недоступен, записи остаются в очереди,
    /// а при закрытии Revit сохраняются локально (%LOCALAPPDATA%\TNov\outbox) и
    /// досылаются при следующем запуске.
    ///
    /// Формат строк в файлах не меняется: записи разделяются "\n", перед первой
    /// записью пачки ставится "\n", если файл уже не пустой.
    /// </summary>
    public static class ServerOutbox
    {
        public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);

        private sealed class Record
        {
            [JsonProperty("p")] public string RelativePath;
            [JsonProperty("l")] public string Line;
        }

        private static readonly object _lock = new object();
        private static readonly object _flushLock = new object();
        private static List<Record> _pending = new List<Record>();
        private static Timer _timer;
        private static bool _restored;

        /// <summary>Текст последней ошибки записи на сервер (null — последняя попытка удалась).</summary>
        public static string LastError { get; private set; }

        /// <summary>Сколько записей ждёт отправки.</summary>
        public static int PendingCount { get { lock (_lock) return _pending.Count; } }

        private static string OutboxFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TNov", "outbox");

        /// <summary>
        /// Поставить строку в очередь на дозапись в файл <paramref name="relativePath"/>
        /// относительно серверной папки. Переводы строк внутри <paramref name="line"/> заменяются пробелами.
        /// </summary>
        public static void AppendLine(string relativePath, string line)
        {
            if (string.IsNullOrEmpty(relativePath) || line == null) return;
            line = line.Replace("\r", " ").Replace("\n", " ");
            lock (_lock)
            {
                _pending.Add(new Record { RelativePath = relativePath.Replace('/', '\\'), Line = line });
                EnsureStarted();
            }
        }

        /// <summary>Запуск таймера и подхват записей, оставшихся с прошлых сессий.</summary>
        public static void Start()
        {
            lock (_lock) EnsureStarted();
        }

        private static void EnsureStarted()
        {
            if (!_restored)
            {
                _restored = true;
                try { _pending.InsertRange(0, RestoreSaved()); } catch (Exception) { }
            }
            if (_timer == null)
                _timer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        }

        /// <summary>
        /// Дописать всё накопленное сейчас. Вызывается таймером и при закрытии Revit.
        /// Возвращает true, если очередь пуста после попытки.
        /// </summary>
        public static bool Flush()
        {
            // Таймер и завершение работы не должны писать одновременно — иначе порядок строк поплывёт.
            if (!Monitor.TryEnter(_flushLock, TimeSpan.FromSeconds(10))) return false;
            try
            {
                List<Record> batch;
                lock (_lock)
                {
                    if (_pending.Count == 0) return true;
                    batch = _pending;
                    _pending = new List<Record>();
                }

                string serverPath = TNovConfigLoad.GetCachedConfig()?.ServerPath;
                var failed = new List<Record>();
                string error = null;

                if (string.IsNullOrEmpty(serverPath))
                {
                    failed.AddRange(batch);
                    error = "Не задана серверная папка";
                }
                else
                {
                    // Группируем по файлу с сохранением порядка появления.
                    foreach (var group in batch.GroupBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            AppendToFile(ServerData.Combine(serverPath, group.Key), group.Select(r => r.Line));
                        }
                        catch (Exception ex)
                        {
                            failed.AddRange(group);
                            error = ex.Message;
                        }
                    }
                }

                lock (_lock)
                {
                    if (failed.Count > 0) _pending.InsertRange(0, failed);
                    LastError = error;
                    return _pending.Count == 0;
                }
            }
            finally
            {
                Monitor.Exit(_flushLock);
            }
        }

        private static void AppendToFile(string fullPath, IEnumerable<string> lines)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) ServerDirectories.Ensure(dir);

            using (var fs = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                var sb = new StringBuilder();
                bool first = fs.Length == 0;
                foreach (string line in lines)
                {
                    if (!first) sb.Append('\n');
                    sb.Append(line);
                    first = false;
                }
                // File.AppendAllText пишет UTF-8 без BOM — делаем так же.
                byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
                fs.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// Завершение работы (OnShutdown): попытка дописать очередь за отведённое время,
        /// остаток сохраняется локально.
        /// </summary>
        public static void Shutdown(TimeSpan timeout)
        {
            try
            {
                _timer?.Dispose();
                _timer = null;

                var flush = System.Threading.Tasks.Task.Run(() => Flush());
                bool done = flush.Wait(timeout) && flush.Result;
                if (!done) SaveLocally();
            }
            catch (Exception)
            {
                SaveLocally();
            }
        }

        private static void SaveLocally()
        {
            List<Record> rest;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                rest = _pending;
                _pending = new List<Record>();
            }
            try
            {
                Directory.CreateDirectory(OutboxFolder);
                string file = Path.Combine(OutboxFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.WriteAllText(file, JsonConvert.SerializeObject(rest));
            }
            catch (Exception) { }
        }

        private static List<Record> RestoreSaved()
        {
            var result = new List<Record>();
            if (!Directory.Exists(OutboxFolder)) return result;

            foreach (string file in Directory.GetFiles(OutboxFolder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                // Файл может одновременно подхватывать второй экземпляр Revit — забираем его переименованием.
                string claimed = file + ".taken";
                try { File.Move(file, claimed); }
                catch (Exception) { continue; }

                try
                {
                    var records = JsonConvert.DeserializeObject<List<Record>>(File.ReadAllText(claimed));
                    if (records != null) result.AddRange(records.Where(r => r?.RelativePath != null && r.Line != null));
                    File.Delete(claimed);
                }
                catch (Exception) { }
            }
            return result;
        }
    }

    /// <summary>
    /// Directory.Exists/CreateDirectory по сети — лишний round-trip на каждый вызов.
    /// Запоминаем папки, которые уже проверили в этом процессе.
    /// </summary>
    public static class ServerDirectories
    {
        private static readonly HashSet<string> _known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Ensure(string fullPath)
        {
            lock (_known)
            {
                if (_known.Contains(fullPath)) return;
            }
            Directory.CreateDirectory(fullPath); // no-op, если папка уже есть
            lock (_known) _known.Add(fullPath);
        }
    }
}
