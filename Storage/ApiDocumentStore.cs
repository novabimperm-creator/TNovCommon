using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TNovApi.Client;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Документы через TNovApi (Postgres в Перми). Каждое удачное чтение кладётся в
    /// локальный кэш %LOCALAPPDATA%\TNov\cache\documents; если API недоступен, Load
    /// отдаёт кэш с FromCache = true — окно показывает данные только для чтения.
    /// Сохранение и опрос без связи бросают <see cref="DocumentStoreUnavailableException"/>.
    /// </summary>
    public sealed class ApiDocumentStore : IDocumentStore
    {
        private readonly TNovApiClient _client;

        public ApiDocumentStore(TNovApiClient client)
        {
            _client = client;
        }

        public string Name => "api";

        public TNovApiClient Client => _client;

        public async Task<StoredDocument> LoadAsync(string kind, string key, CancellationToken cancellationToken = default)
        {
            try
            {
                TNovDocument<JToken> doc = await _client.GetDocumentAsync<JToken>(kind, key, cancellationToken).ConfigureAwait(false);
                StoredDocument result = doc == null ? StoredDocument.Missing(kind, key) : Convert(doc);
                // 404 может прийти и не от API (прокси, чужой сервис на адресе) — кэш с данными
                // «документом нет» не затираем, иначе без связи окно показало бы пустоту.
                DocumentCache.PutUnlessErasing(result);
                return result;
            }
            catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
            {
                StoredDocument cached = DocumentCache.Get(kind, key);
                if (cached != null)
                {
                    cached.FromCache = true;
                    return cached;
                }
                throw new DocumentStoreUnavailableException($"Сервер TNov недоступен: {ex.Message}", ex);
            }
        }

        public async Task<StoredDocument> PollAsync(string kind, string key, long knownVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                PollResult<JToken> poll = await _client.PollDocumentAsync<JToken>(kind, key, knownVersion, cancellationToken).ConfigureAwait(false);
                switch (poll.Status)
                {
                    case PollStatus.NotModified:
                        return null;
                    case PollStatus.NotFound:
                        if (knownVersion == 0) return null;
                        // Кэш с данными не затираем (см. LoadAsync).
                        StoredDocument missing = StoredDocument.Missing(kind, key);
                        DocumentCache.PutUnlessErasing(missing);
                        return missing;
                    default:
                        StoredDocument changed = Convert(poll.Document);
                        DocumentCache.Put(changed);
                        return changed;
                }
            }
            catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
            {
                throw new DocumentStoreUnavailableException($"Сервер TNov недоступен: {ex.Message}", ex);
            }
        }

        public async Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                JToken data = ParseVerbatim(json);
                SaveResult<JToken> result = await _client.SaveDocumentAsync(kind, key, data,
                    expectedVersion == 0 ? (long?)null : expectedVersion, cancellationToken).ConfigureAwait(false);

                StoredDocument current = result.Current == null ? StoredDocument.Missing(kind, key) : Convert(result.Current);
                if (result.Saved) current.Json = json; // сохраняем исходное форматирование, как в файле
                DocumentCache.Put(current);
                return new SaveOutcome { Saved = result.Saved, Current = current };
            }
            catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
            {
                throw new DocumentStoreUnavailableException($"Не удалось сохранить: сервер TNov недоступен ({ex.Message}).", ex);
            }
        }

        public async Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(string kind, CancellationToken cancellationToken = default)
        {
            try
            {
                IReadOnlyList<TNovDocumentInfo> list = await _client.ListDocumentsAsync(kind, null, null, cancellationToken).ConfigureAwait(false);
                return list.Select(d => new StoredDocumentInfo
                {
                    Kind = d.Kind,
                    Key = d.Key,
                    Version = d.Version,
                    UpdatedAtUtc = d.UpdatedAt.UtcDateTime
                }).ToList();
            }
            catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
            {
                throw new DocumentStoreUnavailableException($"Сервер TNov недоступен: {ex.Message}", ex);
            }
        }

        private static StoredDocument Convert(TNovDocument<JToken> doc) => new StoredDocument
        {
            Kind = doc.Kind,
            Key = doc.Key,
            // JToken.ToString(Formatting) нет в Newtonsoft 11 (Revit 2022) и 13.0.1 (Revit 2027) — только SerializeObject.
            Json = doc.Data == null ? null : JsonConvert.SerializeObject(doc.Data, Formatting.Indented),
            Version = doc.Version,
            UpdatedAtUtc = doc.UpdatedAt.UtcDateTime,
            UpdatedBy = doc.UpdatedBy
        };

        /// <summary>
        /// JSON как есть: без превращения строк-дат в DateTime (JToken.Parse сохранил бы их
        /// в другом формате и текст документа на сервере изменился бы).
        /// </summary>
        internal static JToken ParseVerbatim(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                JToken token = JToken.ReadFrom(reader);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment)
                        throw new JsonReaderException("Лишние данные после конца JSON.");
                }
                return token;
            }
        }

        /// <summary>
        /// Сеть, таймаут, 5xx/408/429, освобождённый клиент (сменили адрес API) — «сервер недоступен».
        /// Отмена по токену и прочие 4xx — ошибки вызывающего.
        /// </summary>
        internal static bool IsUnavailable(Exception ex, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1) ex = agg.InnerException;
            if (ex is TNovApiException api)
            {
                int code = (int)api.StatusCode;
                return code >= 500 || code == 408 || code == 429;
            }
            return ex is HttpRequestException || ex is TimeoutException || ex is OperationCanceledException
                || ex is WebException || ex is IOException || ex is System.Net.Sockets.SocketException
                || ex is ObjectDisposedException
                || (ex.InnerException is HttpRequestException || ex.InnerException is System.Net.Sockets.SocketException);
        }
    }

    /// <summary>Локальная копия последних прочитанных документов (для режима «только чтение» без связи).</summary>
    internal static class DocumentCache
    {
        private sealed class Entry
        {
            public long Version { get; set; }
            public string Json { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
            public string UpdatedBy { get; set; }
        }

        private static readonly object _lock = new object();

        private static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TNov", "cache", "documents");

        private static string PathOf(string kind, string key)
        {
            // Ключ — имя модели (кириллица, пробелы, точки); в имени файла — хэш, чтобы не упираться в запрещённые символы.
            string hash;
            using (var sha = SHA1.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 16);
            return Path.Combine(Root, kind, hash + ".json");
        }

        public static void Put(StoredDocument doc)
        {
            try
            {
                string path = PathOf(doc.Kind, doc.Key);
                lock (_lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, JsonConvert.SerializeObject(new Entry
                    {
                        Version = doc.Version, Json = doc.Json, UpdatedAtUtc = doc.UpdatedAtUtc, UpdatedBy = doc.UpdatedBy
                    }));
                }
            }
            catch (Exception) { }
        }

        /// <summary>Как <see cref="Put"/>, но «документа нет» не заменяет закэшированный документ с данными.</summary>
        public static void PutUnlessErasing(StoredDocument doc)
        {
            if (!doc.Exists)
            {
                StoredDocument cached = Get(doc.Kind, doc.Key);
                if (cached != null && cached.Version > 0) return;
            }
            Put(doc);
        }

        public static StoredDocument Get(string kind, string key)
        {
            try
            {
                string path = PathOf(kind, key);
                lock (_lock)
                {
                    if (!File.Exists(path)) return null;
                    Entry e = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(path));
                    if (e == null) return null;
                    return new StoredDocument
                    {
                        Kind = kind, Key = key, Json = e.Json, Version = e.Version,
                        UpdatedAtUtc = e.UpdatedAtUtc, UpdatedBy = e.UpdatedBy
                    };
                }
            }
            catch (Exception) { return null; }
        }
    }
}
