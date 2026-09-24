using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TNovApi.Client;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Вложения чек-листа: фото пунктов и логи автопроверок.
    ///
    /// CheckItem/AutoCheckItem работают с локальными путями ({корень}\{id}\{фото}, {корень}\{n}.txt),
    /// поэтому сервис отдаёт корневые папки:
    ///   файлы — прежние папки на шаре ({модель},checklist_photos, {модель},autocheck_checklogs);
    ///   API  — локальный кэш %LOCALAPPDATA%\TNov\cache\{photos|checklogs}\{хэш модели}; сами фото
    ///          лежат в /api/files (kind checklist-photo, scope = модель, owner = id пункта),
    ///          логи — события autocheck_log (тот же формат, что у утилиты импорта).
    /// Все методы, кроме *Root, вызывать вне UI-потока.
    /// </summary>
    public sealed class ChecklistAttachments
    {
        public const string PhotoKind = "checklist-photo";
        public const string LogEventKind = "autocheck_log";

        private readonly IDocumentStore _store;
        private readonly string _modelKey;

        public ChecklistAttachments(IDocumentStore store, string modelKey)
        {
            _store = store;
            _modelKey = modelKey;
        }

        private TNovApiClient Api => (_store as ApiDocumentStore)?.Client;

        public bool UsesApi => Api != null;

        /// <summary>Корень папок фото ({корень}\{id пункта}\{файл}).</summary>
        public string PhotosRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_modelKey)) return null;
                if (_store is FileDocumentStore files)
                    return StripJson(files.PathOf(DocumentKinds.Checklist, _modelKey)) + "_photos";
                return Path.Combine(CacheRoot, "photos", Hash(_modelKey));
            }
        }

        /// <summary>Корень логов автопроверок ({корень}\{номер}.txt).</summary>
        public string LogsRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_modelKey)) return null;
                if (_store is FileDocumentStore files)
                    return StripJson(files.PathOf(DocumentKinds.AutoCheck, _modelKey)) + "_checklogs";
                return Path.Combine(CacheRoot, "checklogs", Hash(_modelKey));
            }
        }

        // ---------- фото ----------

        /// <summary>
        /// Режим API: скачать фото в локальный кэш, если его там нет. Возвращает true, если файл
        /// появился (нужно обновить картинку в UI). Файловый режим — ничего не делает.
        /// </summary>
        public async Task<bool> EnsurePhotoAsync(Guid itemId, string fileName, string fileId, CancellationToken ct = default)
        {
            if (Api == null || string.IsNullOrEmpty(fileName) || !Guid.TryParse(fileId, out Guid id)) return false;

            string target = Path.Combine(PhotosRoot, itemId.ToString(), fileName);
            if (File.Exists(target)) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + ".part";
            bool found;
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                found = await Api.DownloadFileAsync(id, fs, ct).ConfigureAwait(false);

            if (!found) { TryDelete(temp); return false; }
            if (File.Exists(target)) TryDelete(target);
            File.Move(temp, target);
            return true;
        }

        /// <summary>
        /// Режим API: загрузить фото (уже скопированное в PhotosRoot\{id}\) на сервер.
        /// Возвращает id файла для CheckItem.PhotoFileId; в файловом режиме — null.
        /// </summary>
        public async Task<string> UploadPhotoAsync(Guid itemId, string localPath, CancellationToken ct = default)
        {
            if (Api == null) return null;
            using (var fs = File.OpenRead(localPath))
            {
                TNovFileInfo info = await Api.UploadFileAsync(PhotoKind, _modelKey, itemId.ToString(),
                    Path.GetFileName(localPath), fs, ContentTypeOf(localPath), ct).ConfigureAwait(false);
                return info.Id.ToString();
            }
        }

        /// <summary>Режим API: удалить файл фото на сервере (пункт удалён или фото заменено).</summary>
        public async Task DeletePhotoAsync(string fileId, CancellationToken ct = default)
        {
            if (Api == null || !Guid.TryParse(fileId, out Guid id)) return;
            await Api.DeleteFileAsync(id, ct).ConfigureAwait(false);
        }

        // ---------- логи автопроверок ----------

        /// <summary>
        /// Записать лог прогона проверки <paramref name="number"/>: всегда в LogsRoot\{n}.txt
        /// (на шару или в локальный кэш), в режиме API — ещё и событием autocheck_log.
        /// </summary>
        public async Task SaveLogAsync(int number, string text, string userName, CancellationToken ct = default)
        {
            string root = LogsRoot;
            if (string.IsNullOrEmpty(root)) return;

            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, number + ".txt"), text ?? "");

            if (Api == null) return;
            await Api.PostEventsAsync(new[]
            {
                new TNovEvent
                {
                    Kind = LogEventKind,
                    Ts = DateTimeOffset.UtcNow,
                    User = userName,
                    Machine = Environment.MachineName,
                    Doc = _modelKey,
                    Command = "autocheck",
                    Extra = new JObject { ["file"] = number + ".txt", ["number"] = number, ["text"] = text ?? "", ["source"] = "plugin" }
                }
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Режим API: если лога нет в локальном кэше (прогон был на другой машине) — взять
        /// последний с сервера. Возвращает путь к файлу или null, если лога нет нигде.
        /// </summary>
        public async Task<string> EnsureLogAsync(int number, CancellationToken ct = default)
        {
            string root = LogsRoot;
            if (string.IsNullOrEmpty(root)) return null;
            string path = Path.Combine(root, number + ".txt");

            if (Api == null) return File.Exists(path) ? path : null;

            var events = await Api.QueryEventsAsync(new EventQuery { Kind = LogEventKind, Doc = _modelKey, Limit = 5000 }, ct).ConfigureAwait(false);
            TNovEvent latest = events
                .Where(e => e.Extra != null && e.Extra.Value<int?>("number") == number)
                .OrderByDescending(e => e.Ts ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (latest == null) return File.Exists(path) ? path : null;

            // Свежий лог на сервере мог появиться после нашего локального — берём серверный.
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < (latest.Ts ?? DateTimeOffset.MinValue).UtcDateTime)
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(path, latest.Extra.Value<string>("text") ?? "");
            }
            return path;
        }

        // ---------- служебное ----------

        private static string CacheRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TNov", "cache");

        private static string StripJson(string path) =>
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - 5) : path;

        private static string Hash(string key)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 16);
        }

        private static string ContentTypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                default: return "image/jpeg";
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { }
        }
    }
}
