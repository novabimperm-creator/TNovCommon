using Autodesk.Revit.DB;
using System;
using TNovApi.Client;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Выбор хранилища по TNovConfig.json:
    ///   "ChecklistStorage": "api" + "ApiUrl" (+ "ApiKey") — TNovApi; пока "FileSync" не false —
    ///     с синхронизацией файлов на шаре для старых версий (<see cref="FileSyncDocumentStore"/>);
    ///   иначе (по умолчанию) — файлы на шаре, как раньше.
    /// </summary>
    public static class DocumentStores
    {
        private static readonly object _lock = new object();
        private static TNovApiClient _client;
        private static string _clientSignature;

        public const string StorageApi = "api";
        public const string StorageFiles = "files";

        /// <summary>Хранилище чек-листа, автопроверок и BIM-проверок.</summary>
        public static IDocumentStore ForChecklist() => ForChecklist(TNovConfigLoad.GetCachedConfig());

        public static IDocumentStore ForChecklist(TNovConfig config)
        {
            if (config == null)
                throw new InvalidOperationException("Не прочитан TNovConfig.json.");

            if (string.Equals(config.ChecklistStorage, StorageApi, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(config.ApiUrl))
            {
                var api = new ApiDocumentStore(GetClient(config));
                // Переходный период: файлы на шаре для старых версий плагина ("FileSync": null/true).
                // Шары офисов — "FileSyncShares" из tnovapi.json (своя — совпадающая с ServerPath).
                if (config.FileSync != false
                    && (!string.IsNullOrWhiteSpace(config.ServerPath) || (config.FileSyncShares?.Length ?? 0) > 0))
                    return new FileSyncDocumentStore(api, config.ServerPath, config.FileSyncShares);
                return api;
            }

            return new FileDocumentStore(config.ServerPath);
        }

        /// <summary>
        /// Один HttpClient на процесс Revit: keep-alive экономит TCP/TLS-рукопожатия,
        /// а из удалённого офиса каждое — это ещё один round-trip.
        /// </summary>
        public static TNovApiClient GetClient(TNovConfig config)
        {
            string signature = config.ApiUrl + "|" + config.ApiKey;
            lock (_lock)
            {
                if (_client == null || _clientSignature != signature)
                {
                    // Прежний клиент не освобождаем: им ещё пользуются открытые окна (опрос,
                    // сохранение) — Dispose дал бы им ObjectDisposedException. Смена адреса/ключа
                    // бывает редко; клиент соберёт GC, когда окна закроются.
                    _client = new TNovApiClient(new TNovApiClientOptions
                    {
                        BaseAddress = new Uri(config.ApiUrl),
                        ApiKey = config.ApiKey,
                        UserName = Environment.UserName,
                        // Ключа нет — вход по учётке Windows (Kerberos → токен API). ApiUrl должен быть
                        // полным именем сервера с SPN (tnov-api.talan.udm.ru), не IP и не localhost.
                        UseDefaultCredentials = string.IsNullOrWhiteSpace(config.ApiKey),
                        Timeout = TimeSpan.FromSeconds(10)
                    });
                    _clientSignature = signature;
                }
                return _client;
            }
        }
    }

    public static class DocumentKeys
    {
        /// <summary>
        /// Ключ модели — то же имя, что в projects/{имя},checklist.json: Title без «_пользователь»,
        /// запятые заменены пробелами. null для несохранённой модели. Вызывать в UI-потоке Revit.
        /// </summary>
        public static string ForDocument(Document doc)
        {
            if (doc == null) return null;
            try
            {
                if (string.IsNullOrEmpty(doc.PathName)) return null;
            }
            catch { return null; }

            string docName = doc.Title.Replace(",", " ");
            string userName = doc.Application.Username;
            return docName.Replace("_" + userName, "");
        }
    }
}
