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
    /// Версия — отметка времени записи файла (тики UTC). Проверка версии перед записью
    /// делается «по возможности»: SMB не даёт атомарного сравнения-и-записи, но
    /// окно гонки сжимается с секунд до миллисекунд.
    /// </summary>
    public sealed class FileDocumentStore : IDocumentStore
    {
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
            Task.Run(() => Stamp(PathOf(kind, key)) == knownVersion ? null : Load(kind, key), cancellationToken);

        public Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                string path = PathOf(kind, key);
                lock (_fileLock)
                {
                    if (Stamp(path) != expectedVersion)
                        return new SaveOutcome { Saved = false, Current = Load(kind, key) };

                    Server.ServerDirectories.Ensure(ProjectsFolder);
                    WithRetry(path, () => File.WriteAllText(path, json));
                    return new SaveOutcome
                    {
                        Saved = true,
                        Current = new StoredDocument
                        {
                            Kind = kind, Key = key, Json = json, Version = Stamp(path), UpdatedAtUtc = DateTime.UtcNow
                        }
                    };
                }
            }, cancellationToken);

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
                var fi = new FileInfo(path);
                if (!fi.Exists) return StoredDocument.Missing(kind, key);

                string json = null;
                WithRetry(path, () => json = File.ReadAllText(path, Encoding.UTF8));
                fi.Refresh();
                return new StoredDocument
                {
                    Kind = kind, Key = key, Json = json,
                    Version = fi.LastWriteTimeUtc.Ticks, UpdatedAtUtc = fi.LastWriteTimeUtc
                };
            }
        }

        private static long Stamp(string path)
        {
            var fi = new FileInfo(path); // один запрос атрибутов по SMB
            return fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
        }

        private static void WithRetry(string path, Action action)
        {
            for (int i = 0; i < 3; i++)
            {
                try { action(); return; }
                catch (IOException) when (i < 2) { Thread.Sleep(300); }
            }
        }
    }
}
