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
    /// досылаются при следующем запуске. Очередь в памяти ограничена
    /// <see cref="MaxPending"/> строками — сверх этого записи уходят в тот же локальный
    /// outbox и подхватываются после ближайшей удачной отправки.
    ///
    /// Пачка, которая сейчас пишется на сервер (<c>_inFlight</c>), тоже видна завершению
    /// работы: если запись зависла на недоступной шаре, Shutdown сохранит её локально.
    /// Если зависшая запись потом всё-таки пройдёт, отправленные строки убираются из
    /// локального файла. Возможен дубль, если байты на сервер записались, но закрытие
    /// файла вернуло ошибку (или Revit закрылся раньше, чем запись завершилась) — строка
    /// будет дописана повторно; это допустимо, потеря хуже.
    ///
    /// Формат строк в файлах не меняется: записи разделяются "\n", перед первой
    /// записью пачки ставится "\n", если файл уже не пустой.
    /// </summary>
    public static class ServerOutbox
    {
        public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);

        /// <summary>Сколько строк держать в памяти; дальше — в локальный outbox.</summary>
        public const int MaxPending = 10000;

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
        private static bool _spilled;           // часть очереди лежит в локальном outbox — забрать после удачной отправки

        // Пачка, которая сейчас пишется (под _lock). _inFlightGen = 0 — пачки нет или её уже сохранил SaveLocally.
        private static List<Record> _inFlight = new List<Record>();
        private static int _inFlightGen;
        private static int _flushGen;

        // Что сохранил SaveLocally при завершении (чтобы убрать строки, которые зависшая запись всё-таки отправила).
        private static string _savedFile;
        private static List<Record> _savedRecords;

        /// <summary>Текст последней ошибки записи на сервер (null — последняя попытка удалась).</summary>
        public static string LastError { get; private set; }

        /// <summary>Сколько записей ждёт отправки (в очереди и в текущей пачке).</summary>
        public static int PendingCount
        {
            get { lock (_lock) return _pending.Count + (_inFlightGen != 0 ? _inFlight.Count : 0); }
        }

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
                if (_pending.Count >= MaxPending) SpillPending();
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
                int gen;
                lock (_lock)
                {
                    if (_pending.Count == 0) return true;
                    batch = _pending;
                    _pending = new List<Record>();
                    gen = ++_flushGen;
                    if (gen == 0) gen = ++_flushGen;
                    _inFlight = new List<Record>(batch);
                    _inFlightGen = gen;
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
                        // Завершение работы уже сохранило пачку локально — остальное отправит следующий запуск.
                        lock (_lock)
                            if (_inFlightGen != gen) break;

                        var records = group.ToList();
                        try
                        {
                            AppendToFile(ServerData.Combine(serverPath, group.Key), records.Select(r => r.Line));
                        }
                        catch (Exception ex)
                        {
                            failed.AddRange(records);
                            error = ex.Message;
                            continue;
                        }
                        MarkSent(gen, records);
                    }
                }

                bool restoreSpilled = false;
                lock (_lock)
                {
                    if (_inFlightGen == gen)
                    {
                        _inFlight = new List<Record>();
                        _inFlightGen = 0;
                        if (failed.Count > 0) _pending.InsertRange(0, failed);
                    }
                    // Иначе неотправленное уже лежит в локальном файле SaveLocally — в очередь не возвращаем.
                    LastError = error;
                    restoreSpilled = failed.Count == 0 && _spilled;
                    if (restoreSpilled) _spilled = false;
                }

                if (restoreSpilled)
                {
                    // Связь есть — забрать то, что не поместилось в память; отправится на следующем тике.
                    List<Record> spilled;
                    try { spilled = RestoreSaved(); } catch (Exception) { spilled = new List<Record>(); }
                    lock (_lock) _pending.InsertRange(0, spilled);
                }

                lock (_lock) return _pending.Count == 0;
            }
            finally
            {
                Monitor.Exit(_flushLock);
            }
        }

        /// <summary>Группа записана на сервер: убрать её из текущей пачки или, если пачку уже сохранил SaveLocally, из его файла.</summary>
        private static void MarkSent(int gen, List<Record> sent)
        {
            var set = new HashSet<Record>(sent);
            lock (_lock)
            {
                if (_inFlightGen == gen)
                {
                    _inFlight.RemoveAll(set.Contains);
                    return;
                }
                if (_savedRecords == null || _savedFile == null) return;
                int before = _savedRecords.Count;
                _savedRecords.RemoveAll(set.Contains);
                if (_savedRecords.Count == before) return;
                try
                {
                    if (!File.Exists(_savedFile)) return; // уже забрал другой экземпляр Revit — возможен дубль
                    if (_savedRecords.Count == 0) File.Delete(_savedFile);
                    else File.WriteAllText(_savedFile, JsonConvert.SerializeObject(_savedRecords));
                }
                catch (Exception) { }
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

        /// <summary>Очередь и зависшую пачку — в локальный outbox (порядок: сначала пачка, она старше).</summary>
        private static void SaveLocally()
        {
            lock (_lock)
            {
                var rest = new List<Record>();
                if (_inFlightGen != 0)
                {
                    rest.AddRange(_inFlight);
                    _inFlight = new List<Record>();
                    _inFlightGen = 0; // Flush увидит, что пачку забрали, и не вернёт её в очередь
                }
                rest.AddRange(_pending);
                _pending = new List<Record>();
                if (rest.Count == 0) return;

                string file = WriteLocalFile(rest);
                if (file != null)
                {
                    _savedFile = file;
                    _savedRecords = rest;
                }
            }
        }

        /// <summary>Под _lock: очередь переросла <see cref="MaxPending"/> — в локальный outbox.</summary>
        private static void SpillPending()
        {
            if (WriteLocalFile(_pending) == null) return; // диск недоступен — держим в памяти
            _pending = new List<Record>();
            _spilled = true;
        }

        private static string WriteLocalFile(List<Record> records)
        {
            try
            {
                Directory.CreateDirectory(OutboxFolder);
                string file = Path.Combine(OutboxFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                // Через временное имя (не *.json): другой экземпляр Revit не заберёт недописанный файл.
                string temp = file + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(records));
                File.Move(temp, file);
                return file;
            }
            catch (Exception)
            {
                return null;
            }
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
