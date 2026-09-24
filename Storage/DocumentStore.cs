using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TNovCommon.Storage
{
    /// <summary>Виды общих документов модели (kind в API, суффикс имени файла на шаре).</summary>
    public static class DocumentKinds
    {
        public const string Checklist = "checklist";
        public const string AutoCheck = "autocheck";
        public const string BimCheck = "bimcheck";

        /// <summary>Суффикс файла projects/{модель},{суффикс}.json для файлового хранилища.</summary>
        public static string FileSuffix(string kind)
        {
            switch (kind)
            {
                case BimCheck: return "BIM проверки";
                default: return kind;
            }
        }

        public static IReadOnlyList<string> ChecklistKinds { get; } = new[] { Checklist, AutoCheck, BimCheck };
    }

    /// <summary>
    /// Документ из хранилища. Version = 0 — документа нет (создавать с expectedVersion = 0).
    /// Json — сырой JSON (тот же формат, что в файлах projects/*.json); null, если документа нет.
    /// </summary>
    public sealed class StoredDocument
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public string Json { get; set; }
        public long Version { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }

        /// <summary>Данные взяты из локального кэша: сервер недоступен, правка запрещена.</summary>
        public bool FromCache { get; set; }

        public bool Exists => Version != 0;

        public static StoredDocument Missing(string kind, string key) =>
            new StoredDocument { Kind = kind, Key = key, Json = null, Version = 0 };
    }

    public sealed class SaveOutcome
    {
        /// <summary>true — сохранено; false — конфликт версий, в <see cref="Current"/> актуальный документ.</summary>
        public bool Saved { get; set; }
        public StoredDocument Current { get; set; }
    }

    /// <summary>Информация о документе без данных (для отчётов и списков).</summary>
    public sealed class StoredDocumentInfo
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public long Version { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }

    /// <summary>Хранилище недоступно (нет сети/API, ошибка сервера) и кэша тоже нет.</summary>
    public sealed class DocumentStoreUnavailableException : Exception
    {
        public DocumentStoreUnavailableException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Общие документы модели (чек-лист, автопроверки, BIM-проверки…).
    /// Две реализации: файлы на шаре (<see cref="FileDocumentStore"/>) и HTTP API
    /// (<see cref="ApiDocumentStore"/>). Все методы можно вызывать вне UI-потока и
    /// без Revit API; ключ модели вычисляется заранее (<see cref="DocumentKeys"/>).
    /// </summary>
    public interface IDocumentStore
    {
        /// <summary>Короткое имя для логов/подсказок: "files" или "api".</summary>
        string Name { get; }

        Task<StoredDocument> LoadAsync(string kind, string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Дешёвая проверка изменений: null — версия не изменилась, иначе актуальный документ
        /// (в т.ч. Missing, если документ удалён).
        /// </summary>
        Task<StoredDocument> PollAsync(string kind, string key, long knownVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохранить, если в хранилище всё ещё версия <paramref name="expectedVersion"/> (0 — документа нет).
        /// При конфликте возвращает Saved = false и актуальный документ, исключение не бросает.
        /// </summary>
        Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(string kind, CancellationToken cancellationToken = default);
    }

    public static class DocumentStoreExtensions
    {
        /// <summary>
        /// Изменить документ с повтором при конфликте: загрузить, применить <paramref name="mutate"/>
        /// к актуальному JSON (null — документа нет), сохранить; если кто-то успел раньше —
        /// применить то же действие к свежей копии и повторить. Так правки разных людей
        /// по разным пунктам не затирают друг друга.
        /// <paramref name="mutate"/> возвращает новый JSON или null — «ничего не менять».
        /// </summary>
        public static async Task<StoredDocument> UpdateAsync(this IDocumentStore store, string kind, string key,
            Func<string, string> mutate, StoredDocument known = null, int maxAttempts = 5,
            CancellationToken cancellationToken = default)
        {
            StoredDocument current = known ?? await store.LoadAsync(kind, key, cancellationToken).ConfigureAwait(false);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (current.FromCache)
                    throw new DocumentStoreUnavailableException("Сервер недоступен — правка невозможна.", null);

                string updated = mutate(current.Json);
                if (updated == null) return current;

                SaveOutcome outcome = await store.SaveAsync(kind, key, updated, current.Version, cancellationToken).ConfigureAwait(false);
                if (outcome.Saved) return outcome.Current;

                current = outcome.Current ?? await store.LoadAsync(kind, key, cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException($"Не удалось сохранить {kind} «{key}»: документ постоянно меняется другими пользователями.");
        }
    }
}
