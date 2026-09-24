using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Документы в файлах на шаре: {ServerPath}projects/{ключ},{суффикс}.json — формат и
    /// расположение прежние, чтобы файловый режим оставался рабочим откатом.
    /// Версия — отметка времени записи файла (тики UTC). Проверка версии и замена идут под
    /// файлом-замком ~{имя}.lock (межпроцессно, в т.ч. между машинами); старые версии плагина
    /// замок не знают — с ними проверка прежняя, «по возможности» (окно гонки — миллисекунды).
    ///
    /// Запись — во временный файл рядом и замена (File.Replace / File.Move): читатель никогда
    /// не видит пустой или недописанный файл. Чтение — с отметкой до и после и проверкой JSON;
    /// если файл меняли прямо во время чтения — повтор.
    /// «Документа нет» сообщается только при доступной сетевой папке; иначе — IOException,
    /// и окно переходит в «только просмотр», а не показывает пустой редактируемый список.
    /// </summary>
    public sealed class FileDocumentStore : IDocumentStore
    {
        private const int ReadAttempts = 5;
        private const int WriteAttempts = 5;
        private const int LockTimeoutMs = 5000;

        private static readonly object _fileLock = new object();
        private readonly string _serverPath;

        public FileDocumentStore(string serverPath)
        {
            _serverPath = serverPath;
        }

        public string Name => "files";

        private string ProjectsFolder => Server.ServerData.Combine(_serverPath, "projects");

        public string PathOf(string kind, string key) =>
            Path.Combine(ProjectsFolder, $"{key},{DocumentKinds.FileSuffix(kind)}.json");

        public Task<StoredDocument> LoadAsync(string kind, string key, CancellationToken cancellationToken = default) =>
            Task.Run(() => Load(kind, key), cancellationToken);

        public Task<StoredDocument> PollAsync(string kind, string key, long knownVersion, CancellationToken cancellationToken = default) =>
            // Документа не было и нет — перепроверка «через миг» не нужна (одна отметка по SMB, как раньше).
            Task.Run(() => StampChecked(PathOf(kind, key), settle: knownVersion != 0) == knownVersion ? null : Load(kind, key), cancellationToken);

        public Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                string path = PathOf(kind, key);
                lock (_fileLock)
                {
                    if (StampChecked(path) != expectedVersion)
                        return new SaveOutcome { Saved = false, Current = Load(kind, key) };

                    Server.ServerDirectories.Ensure(ProjectsFolder);

                    // Межпроцессная блокировка (другие Revit, другие машины): проверка отметки и замена
                    // идут под ней, иначе двое, проверившие одну версию, затёрли бы друг друга.
                    using (AcquireWriteLock(path))
                    {
                        // Временный файл в той же папке: замена в пределах одного тома — переименование.
                        string temp = Path.Combine(ProjectsFolder, "~" + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                        try
                        {
                            File.WriteAllText(temp, json);
                            for (int i = 0; ; i++)
                            {
                                // Отметка — перед каждой попыткой: пока ждали снятия блокировки, файл мог записать другой.
                                if (StampChecked(path) != expectedVersion)
                                    return new SaveOutcome { Saved = false, Current = Load(kind, key) };
                                try
                                {
                                    ReplaceWith(temp, path, expectedVersion != 0, lastAttempt: i == WriteAttempts - 1);
                                    break;
                                }
                                // Access denied бывает, пока чужая замена того же файла не завершилась.
                                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && i < WriteAttempts - 1)
                                {
                                    Thread.Sleep(200);
                                }
                            }
                        }
                        finally
                        {
                            TryDelete(temp);
                        }

                        return new SaveOutcome
                        {
                            Saved = true,
                            Current = new StoredDocument
                            {
                                Kind = kind, Key = key, Json = json, Version = Stamp(path), UpdatedAtUtc = DateTime.UtcNow
                            }
                        };
                    }
                }
            }, cancellationToken);

        /// <summary>
        /// Файл-замок ~{имя}.lock (FileShare.None, удаляется при закрытии — после падения процесса
        /// не остаётся; SMB соблюдает режимы общего доступа между машинами). Старые версии плагина
        /// замок не знают — для них защита прежняя, «по возможности».
        /// Не удалось взять за <see cref="LockTimeoutMs"/> — пишем без замка (как раньше), а не отказываем.
        /// </summary>
        private IDisposable AcquireWriteLock(string path)
        {
            string lockPath = Path.Combine(ProjectsFolder, "~" + Path.GetFileName(path) + ".lock");
            var started = DateTime.UtcNow;
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if ((DateTime.UtcNow - started).TotalMilliseconds > LockTimeoutMs)
                    {
                        try { Logger.Log($"Не удалось взять блокировку {lockPath}: {ex.Message} — запись без неё", 2); } catch (Exception) { }
                        return null;
                    }
                    Thread.Sleep(30);
                }
            }
        }

        public Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(string kind, CancellationToken cancellationToken = default) =>
            Task.Run<IReadOnlyList<StoredDocumentInfo>>(() =>
            {
                string suffix = "," + DocumentKinds.FileSuffix(kind) + ".json";
                if (!Directory.Exists(ProjectsFolder)) return new StoredDocumentInfo[0];
                return new DirectoryInfo(ProjectsFolder)
                    .EnumerateFiles("*" + suffix)
                    .Where(f => f.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .Select(f => new StoredDocumentInfo
                    {
                        Kind = kind,
                        Key = f.Name.Substring(0, f.Name.Length - suffix.Length),
                        Version = f.LastWriteTimeUtc.Ticks,
                        UpdatedAtUtc = f.LastWriteTimeUtc
                    })
                    .ToList();
            }, cancellationToken);

        private StoredDocument Load(string kind, string key)
        {
            string path = PathOf(kind, key);
            lock (_fileLock)
            {
                string problem = null;
                for (int attempt = 0; attempt < ReadAttempts; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(200);

                    long before = StampChecked(path);
                    if (before == 0) return StoredDocument.Missing(kind, key);

                    string json;
                    try
                    {
                        json = ReadText(path);
                    }
                    catch (FileNotFoundException) { problem = "файл исчез во время чтения"; continue; }
                    catch (IOException ex) when (attempt < ReadAttempts - 1) { problem = ex.Message; continue; }

                    long after = Stamp(path);
                    if (after != before) { problem = "файл изменился во время чтения"; continue; }
                    if (!IsValidJson(json)) { problem = "пустой или недописанный JSON"; continue; }

                    return new StoredDocument
                    {
                        Kind = kind, Key = key, Json = json,
                        Version = after, UpdatedAtUtc = new DateTime(after, DateTimeKind.Utc)
                    };
                }
                // Не «документа нет»: иначе окно показало бы пустой список и разрешило бы его сохранить.
                throw new IOException($"Не удалось прочитать {path}: {problem}");
            }
        }

        /// <summary>
        /// Замена файла: существующий — File.Replace (атомарно для читателей), нового нет — File.Move
        /// (падает, если файл успели создать — следующая попытка увидит конфликт версий).
        /// Если Replace не поддерживается (NAS) — на последней попытке копирование поверх:
        /// недописанный файл читатель отбросит по проверке JSON.
        /// </summary>
        private static void ReplaceWith(string temp, string path, bool exists, bool lastAttempt)
        {
            if (!exists)
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (lastAttempt && (ex is PlatformNotSupportedException || (ex is IOException && !IsLocked(ex)))
                                       && File.Exists(temp))
            {
                File.Copy(temp, path, true);
            }
        }

        /// <summary>Файл открыт другим (старые версии читают без FileShare.Delete) — ждать, а не копировать поверх.</summary>
        private static bool IsLocked(Exception ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33 || code == 1175 || code == 1176; // sharing/lock violation, unable to remove/move replaced
        }

        private static string ReadText(string path)
        {
            // FileShare.Delete — чтобы наше чтение не мешало File.Replace другого пользователя.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        /// <summary>Полный синтаксически верный JSON (пустой и обрезанный — нет). Даты не разбираем — быстрее.</summary>
        public static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    bool any = false;
                    while (reader.Read()) any = true;
                    return any;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Отметка файла; 0 — файла нет. FileInfo.Exists возвращает false и при обрыве сети,
        /// поэтому «нет» подтверждаем доступностью папки: недоступна — IOException.
        /// </summary>
        private long StampChecked(string path, bool settle = true)
        {
            long stamp = Stamp(path);
            if (stamp != 0) return stamp;

            bool reachable = Directory.Exists(ProjectsFolder)
                || (!string.IsNullOrEmpty(_serverPath) && Directory.Exists(_serverPath)); // папки projects ещё нет — это не сбой
            if (!reachable)
                throw new IOException($"Сетевая папка недоступна: {ProjectsFolder}");

            // Папка доступна — перепроверяем сам файл. File.Replace — не одно переименование:
            // на миг файла под этим именем нет, поэтому «нет» — только если его нет и чуть позже.
            stamp = Stamp(path);
            if (stamp != 0) return stamp;
            if (!settle) return 0;
            Thread.Sleep(100);
            return Stamp(path);
        }

        private static long Stamp(string path)
        {
            var fi = new FileInfo(path); // один запрос атрибутов по SMB
            return fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
