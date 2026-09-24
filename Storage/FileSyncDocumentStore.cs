using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TNovApi.Client;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Переходный период после перевода чек-листов в API ("ChecklistStorage": "api", "FileSync" ≠ false):
    /// основное хранилище — TNovApi, но файлы на шаре ({ServerPath}projects\{модель},checklist.json …)
    /// поддерживаются в актуальном виде для пользователей, которые ещё не обновили плагин.
    ///
    ///  • Load: документ из API + одна отметка времени файла. Файл изменился со времени прошлой
    ///    синхронизации (правка старой версии) — трёхстороннее слияние (<see cref="FileSyncMerge"/>)
    ///    и запись результата в API; фото из файла загружаются в /api/files, логи автопроверок
    ///    уходят событиями autocheck_log.
    ///  • Save: после удачного сохранения в API тот же документ пишется в файл (без photo_file_id,
    ///    Formatting.Indented, UTF-8 без BOM — как пишет старая версия), новые фото и логи копируются
    ///    на шару. Если файл успели изменить — сначала слияние. Ошибка записи в файл сохранение не
    ///    отменяет: в лог, следующий Load сверит.
    ///  • Poll и List — только API.
    ///
    /// Состояние синхронизации хранится в API: документ filesync-{kind} / {модель}:
    /// { fileStamp (тики LastWriteTimeUtc), fileHash (sha256 текста), baseJson (содержимое файла
    ///   на момент синхронизации), writtenVersion / writtenAt (версия API, записанная в файл, и её UpdatedAt),
    ///   syncedAt, syncedBy }.
    /// Его же пишет TNovApi.Import при переносе.
    ///
    /// Сетевая папка недоступна или отвечает дольше <see cref="StatTimeout"/> — работаем только
    /// с API и не трогаем шару <see cref="UnreachableBackoff"/>.
    /// </summary>
    public sealed class FileSyncDocumentStore : IDocumentStore, IApiDocumentStore
    {
        public const string StateKindPrefix = "filesync-";

        internal static TimeSpan StatTimeout = TimeSpan.FromSeconds(5);
        internal static TimeSpan UnreachableBackoff = TimeSpan.FromSeconds(60);
        /// <summary>Сколько Save ждёт записи в файл; дольше — запись доделывается в фоне.</summary>
        internal static TimeSpan DualWriteWait = TimeSpan.FromSeconds(10);
        /// <summary>Для тестов: дублирует сообщения лога.</summary>
        internal static Action<string> LogHook = null;

        private const int WriteAttempts = 3;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _unreachableUntil =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly ApiDocumentStore _api;
        private readonly FileDocumentStore _files;
        private readonly string _serverPath;

        public FileSyncDocumentStore(ApiDocumentStore api, string serverPath)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _serverPath = serverPath;
            _files = new FileDocumentStore(serverPath);
        }

        public string Name => "api";

        public TNovApiClient Client => _api.Client;

        public ApiDocumentStore Api => _api;

        public FileDocumentStore Files => _files;

        private static bool IsSynced(string kind) => DocumentKinds.ChecklistKinds.Contains(kind);

        // ================================================================ IDocumentStore

        public async Task<StoredDocument> LoadAsync(string kind, string key, CancellationToken cancellationToken = default)
        {
            if (!IsSynced(kind)) return await _api.LoadAsync(kind, key, cancellationToken).ConfigureAwait(false);

            // Документ, состояние и отметка файла — параллельно: один round-trip к API и один запрос по SMB.
            Task<StoredDocument> docTask = _api.LoadAsync(kind, key, cancellationToken);
            Task<StateLoad> stateTask = TryLoadStateAsync(kind, key, cancellationToken);
            Task<FileStat> statTask = StatAsync(kind, key);

            StoredDocument doc = await docTask.ConfigureAwait(false);
            if (doc.FromCache) return doc; // API недоступен — сливать некуда

            try
            {
                FileStat stat = await statTask.ConfigureAwait(false);
                if (!stat.Reachable || !stat.Exists) return doc;
                StateLoad state = await stateTask.ConfigureAwait(false);
                if (!state.Ok) return doc;
                if (state.State != null && state.State.FileStamp == stat.Stamp) return doc; // ничего не менялось

                SemaphoreSlim gate = Gate(kind, key);
                bool contended = !gate.Wait(0);
                if (contended) await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (contended)
                    {
                        // Пока ждали, синхронизацию мог сделать другой (окно, пакетный прогон).
                        state = await TryLoadStateAsync(kind, key, cancellationToken).ConfigureAwait(false);
                        if (!state.Ok) return doc;
                        doc = await _api.LoadAsync(kind, key, cancellationToken).ConfigureAwait(false);
                        if (doc.FromCache) return doc;
                    }

                    SyncResult sync = await SyncFromFileAsync(kind, key, doc, state.State, stat, cancellationToken).ConfigureAwait(false);
                    if (sync == null) return doc;

                    StoredDocument result = sync.Merged ?? doc;
                    FileSyncState newState = sync.State;
                    // После слияния в API есть то, чего нет в файле (правки API, победившие в конфликте), —
                    // сразу показываем их и старым версиям.
                    if (sync.Merged != null && !FileSyncMerge.Equivalent(kind, sync.Theirs, ApiDocumentStore.ParseVerbatim(result.Json)))
                    {
                        FileSyncState written = await TryWriteFileAsync(kind, key, result, sync.Theirs, sync.State.FileStamp, cancellationToken).ConfigureAwait(false);
                        if (written != null) newState = written;
                    }
                    await SaveStateAsync(kind, key, newState, sync.StateVersion, cancellationToken).ConfigureAwait(false);
                    return result;
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log($"{kind} / {key}: синхронизация с файлом не удалась — {ex.GetType().Name}: {ex.Message}", 2);
                return doc;
            }
        }

        public Task<StoredDocument> PollAsync(string kind, string key, long knownVersion, CancellationToken cancellationToken = default) =>
            _api.PollAsync(kind, key, knownVersion, cancellationToken);

        public Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(string kind, CancellationToken cancellationToken = default) =>
            _api.ListAsync(kind, cancellationToken);

        public async Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default)
        {
            SaveOutcome outcome = await _api.SaveAsync(kind, key, json, expectedVersion, cancellationToken).ConfigureAwait(false);
            if (!outcome.Saved || !IsSynced(kind) || IsShareBackedOff()) return outcome;

            Task<StoredDocument> work = DualWriteAsync(kind, key, outcome.Current);
            Task first = await Task.WhenAny(work, Task.Delay(DualWriteWait)).ConfigureAwait(false);
            if (first != work)
            {
                Log($"{kind} / {key}: запись в файл идёт дольше {DualWriteWait.TotalSeconds:0} с — доделывается в фоне", 2);
                _ = work.ContinueWith(t => Log($"{kind} / {key}: запись в файл не удалась — {t.Exception?.GetBaseException().Message}", 2),
                    TaskContinuationOptions.OnlyOnFaulted);
                return outcome;
            }
            try
            {
                StoredDocument merged = await work.ConfigureAwait(false);
                // Слияние с файлом дало более новую версию в API — её и отдаём окну.
                if (merged != null && merged.Version > outcome.Current.Version)
                    return new SaveOutcome { Saved = true, Current = merged };
            }
            catch (Exception ex)
            {
                Log($"{kind} / {key}: запись в файл не удалась — {ex.GetType().Name}: {ex.Message}", 2);
            }
            return outcome;
        }

        // ================================================================ дублирующая запись

        /// <summary>Возвращает документ API после слияния с файлом (если оно понадобилось) или null.</summary>
        private async Task<StoredDocument> DualWriteAsync(string kind, string key, StoredDocument saved)
        {
            SemaphoreSlim gate = Gate(kind, key);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                StoredDocument latest = saved;
                StoredDocument merged = null;
                for (int attempt = 0; attempt < WriteAttempts; attempt++)
                {
                    StateLoad state = await TryLoadStateAsync(kind, key, CancellationToken.None).ConfigureAwait(false);
                    if (!state.Ok) return merged;
                    FileStat stat = await StatAsync(kind, key).ConfigureAwait(false);
                    if (!stat.Reachable) return merged;

                    FileSyncState current = state.State;
                    long stateVersion = state.Version;
                    JToken fileBefore = current?.BaseJson;
                    long fileStamp = stat.Exists ? stat.Stamp : 0;
                    if (stat.Exists && (current == null || current.FileStamp != stat.Stamp))
                    {
                        // Файл изменили (старая версия) после нашей последней записи — сначала забрать правку.
                        SyncResult sync = await SyncFromFileAsync(kind, key, latest, current, stat, CancellationToken.None).ConfigureAwait(false);
                        if (sync != null)
                        {
                            if (sync.Merged != null) latest = merged = sync.Merged;
                            current = sync.State;
                            stateVersion = sync.StateVersion;
                            fileBefore = sync.Theirs;
                            fileStamp = sync.State.FileStamp;
                        }
                    }

                    if (current != null && current.WrittenVersion > latest.Version && current.WrittenAt != 0
                        && latest.UpdatedAtUtc.HasValue && current.WrittenAt >= latest.UpdatedAtUtc.Value.Ticks)
                        return merged; // в файле уже более новая версия (записал другой пользователь)

                    JToken data = ApiDocumentStore.ParseVerbatim(latest.Json);
                    if (fileStamp != 0 && fileBefore != null && current != null && current.FileStamp == fileStamp
                        && FileSyncMerge.Equivalent(kind, fileBefore, data))
                    {
                        // Содержимое для старых версий не изменилось (например, только photo_file_id).
                        if (current.WrittenVersion != latest.Version || current != state.State)
                        {
                            current.WrittenVersion = latest.Version;
                            current.WrittenAt = latest.UpdatedAtUtc?.Ticks ?? 0;
                            await SaveStateAsync(kind, key, current, stateVersion, CancellationToken.None).ConfigureAwait(false);
                        }
                        return merged;
                    }

                    FileSyncState written = await TryWriteFileAsync(kind, key, latest, fileBefore, fileStamp, CancellationToken.None).ConfigureAwait(false);
                    if (written != null)
                    {
                        await SaveStateAsync(kind, key, written, stateVersion, CancellationToken.None).ConfigureAwait(false);
                        return merged;
                    }
                    // Файл изменился между проверкой и записью — ещё раз со слиянием.
                    if (current != state.State && current != null)
                        await SaveStateAsync(kind, key, current, stateVersion, CancellationToken.None).ConfigureAwait(false);
                }
                Log($"{kind} / {key}: файл постоянно меняется — не записан, сверится при следующем открытии", 2);
                return merged;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Записать документ API в файл, если файл всё ещё с отметкой <paramref name="expectedStamp"/>
        /// (0 — файла нет). Сначала вложения (фото, логи), потом JSON — старая версия, увидев новый
        /// пункт, сразу найдёт его фото. Возвращает новое состояние или null (файл успели изменить).
        /// </summary>
        private async Task<FileSyncState> TryWriteFileAsync(string kind, string key, StoredDocument doc, JToken fileBefore,
            long expectedStamp, CancellationToken ct)
        {
            JToken data = ApiDocumentStore.ParseVerbatim(doc.Json);
            try
            {
                await CopyAttachmentsToShareAsync(kind, key, data, fileBefore, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log($"{kind} / {key}: не удалось скопировать вложения на шару — {ex.Message}", 2);
            }

            JToken fileData = FileSyncMerge.StripPhotoIds(data);
            string text = FileSyncMerge.Serialize(fileData);
            SaveOutcome outcome = await _files.SaveAsync(kind, key, text, expectedStamp, ct).ConfigureAwait(false);
            if (!outcome.Saved) return null;
            Log($"{kind} / {key}: v{doc.Version} записана в файл", 2);
            return new FileSyncState
            {
                FileStamp = outcome.Current.Version,
                FileHash = FileSyncMerge.Hash(text),
                BaseJson = fileData,
                WrittenVersion = doc.Version,
                WrittenAt = doc.UpdatedAtUtc?.Ticks ?? 0
            };
        }

        /// <summary>Фото, которых ещё нет на шаре (из локального кэша или /api/files), и логи прогонов с этой машины.</summary>
        private async Task CopyAttachmentsToShareAsync(string kind, string key, JToken data, JToken fileBefore, CancellationToken ct)
        {
            var local = new ChecklistAttachments(this, key);
            var share = new ChecklistAttachments(_files, key);
            var before = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            string keyName = kind == DocumentKinds.AutoCheck ? "Number" : "id";
            foreach (JObject o in (fileBefore as JArray ?? new JArray()).OfType<JObject>())
            {
                string k = o[keyName]?.ToString();
                if (!string.IsNullOrEmpty(k) && !before.ContainsKey(k)) before[k] = o;
            }

            if (kind == DocumentKinds.Checklist)
            {
                foreach (JObject item in (data as JArray ?? new JArray()).OfType<JObject>())
                {
                    string photo = item.Value<string>(FileSyncMerge.PhotoProperty);
                    string idText = item.Value<string>("id");
                    if (string.IsNullOrEmpty(photo) || !Guid.TryParse(idText, out Guid id)) continue;
                    if (before.TryGetValue(idText, out JObject old) && old.Value<string>(FileSyncMerge.PhotoProperty) == photo) continue;

                    string target = SafePaths.PhotoPath(share.PhotosRoot, id, photo);
                    if (target == null || File.Exists(target)) continue;
                    string source = SafePaths.PhotoPath(local.PhotosRoot, id, photo);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + "." + Guid.NewGuid().ToString("N") + ".part";
                    try
                    {
                        if (source != null && File.Exists(source))
                        {
                            File.Copy(source, temp, true);
                        }
                        else if (Guid.TryParse(item.Value<string>(FileSyncMerge.PhotoFileIdProperty), out Guid fileId))
                        {
                            bool found;
                            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                                found = await Client.DownloadFileAsync(fileId, fs, ct).ConfigureAwait(false);
                            if (!found) continue;
                        }
                        else continue;
                        if (!File.Exists(target)) File.Move(temp, target);
                    }
                    finally
                    {
                        TryDelete(temp);
                    }
                }
            }
            else if (kind == DocumentKinds.AutoCheck)
            {
                string localRoot = local.LogsRoot, shareRoot = share.LogsRoot;
                if (string.IsNullOrEmpty(localRoot) || string.IsNullOrEmpty(shareRoot)) return;
                foreach (JObject item in (data as JArray ?? new JArray()).OfType<JObject>())
                {
                    if (!(item["Number"] is JValue n) || n.Type != JTokenType.Integer) continue;
                    if (string.IsNullOrWhiteSpace(item.Value<string>("title"))) continue;
                    if (before.TryGetValue(n.ToString(), out JObject old)
                        && FileSyncMerge.ValueEquals(old[FileSyncMerge.CreatedAtProperty], item[FileSyncMerge.CreatedAtProperty]))
                        continue;
                    string source = Path.Combine(localRoot, n.Value<int>().ToString(CultureInfo.InvariantCulture) + ".txt");
                    if (!File.Exists(source)) continue;
                    Directory.CreateDirectory(shareRoot);
                    File.Copy(source, Path.Combine(shareRoot, Path.GetFileName(source)), true);
                }
            }
        }

        // ================================================================ слияние из файла

        private sealed class SyncResult
        {
            /// <summary>Документ API после применения правок из файла; null — применять было нечего.</summary>
            public StoredDocument Merged;
            /// <summary>Содержимое файла, с которым сверились.</summary>
            public JToken Theirs;
            /// <summary>Новое состояние (base = файл); ещё не сохранено.</summary>
            public FileSyncState State;
            public long StateVersion;
        }

        /// <summary>
        /// Файл изменился со времени <paramref name="state"/> (или состояния ещё нет): прочитать,
        /// применить правки к API. Состояние не сохраняет — это делает вызывающий (после возможной
        /// записи в файл). null — файла нет.
        /// </summary>
        private async Task<SyncResult> SyncFromFileAsync(string kind, string key, StoredDocument apiDoc, FileSyncState state,
            FileStat stat, CancellationToken ct)
        {
            long stateVersion = state?.DocVersion ?? 0;
            StoredDocument file = await _files.LoadAsync(kind, key, ct).ConfigureAwait(false);
            if (!file.Exists) return null;

            string hash = FileSyncMerge.Hash(file.Json);
            JToken theirs = ApiDocumentStore.ParseVerbatim(file.Json);
            var newState = new FileSyncState
            {
                FileStamp = file.Version,
                FileHash = hash,
                BaseJson = theirs,
                WrittenVersion = state?.WrittenVersion ?? 0,
                WrittenAt = state?.WrittenAt ?? 0
            };
            var result = new SyncResult { Theirs = theirs, State = newState, StateVersion = stateVersion };

            if (state != null && hash == state.FileHash)
                return result; // файл переписан без изменений — обновить только отметку

            JToken ours = string.IsNullOrEmpty(apiDoc.Json) ? new JArray() : ApiDocumentStore.ParseVerbatim(apiDoc.Json);
            JToken baseToken;
            if (state != null)
            {
                baseToken = state.BaseJson;
            }
            else
            {
                // Первое открытие после перевода (импорт не записал состояние).
                if (FileSyncMerge.Equivalent(kind, theirs, ours))
                {
                    newState.WrittenVersion = apiDoc.Version;
                    newState.WrittenAt = apiDoc.UpdatedAtUtc?.Ticks ?? 0;
                    return result;
                }
                DateTime fileTime = new DateTime(file.Version, DateTimeKind.Utc);
                if (apiDoc.Exists && apiDoc.UpdatedAtUtc.HasValue && apiDoc.UpdatedAtUtc.Value >= fileTime)
                {
                    Log($"{kind} / {key}: состояния нет, документ в API новее файла — правки файла не применяются", 2);
                    return result;
                }
                // Файл новее — всё, чем он отличается от API, считаем правками файла.
                Log($"{kind} / {key}: состояния нет, файл новее документа в API — отличия файла применяются", 2);
                baseToken = ours;
            }

            result.Merged = await ApplyFileEditsAsync(kind, key, apiDoc, baseToken, theirs, ct).ConfigureAwait(false);
            return result;
        }

        private async Task<StoredDocument> ApplyFileEditsAsync(string kind, string key, StoredDocument apiDoc,
            JToken baseToken, JToken theirs, CancellationToken ct)
        {
            // Фото: загрузить до слияния (mutate синхронный и может повторяться).
            var photoIds = kind == DocumentKinds.Checklist
                ? await UploadFilePhotosAsync(key, apiDoc, baseToken, theirs, ct).ConfigureAwait(false)
                : new Dictionary<string, KeyValuePair<string, string>>();

            FileSyncMerge.Result last = null;
            StoredDocument merged = await _api.UpdateAsync(kind, key, json =>
            {
                JToken ours = string.IsNullOrEmpty(json) ? new JArray() : ApiDocumentStore.ParseVerbatim(json);
                last = FileSyncMerge.Apply(kind, baseToken, theirs, ours, photoIds);
                return last.Changed ? FileSyncMerge.Serialize(last.Merged) : null;
            }, apiDoc, cancellationToken: ct).ConfigureAwait(false);

            if (last != null)
                foreach (string note in last.Notes)
                    Log($"{kind} / {key}: {note}", 2);

            // Загруженные, но не понадобившиеся фото (победил API) — удалить.
            foreach (var pair in photoIds)
            {
                if (last != null && last.Changed && last.PhotosApplied.Contains(pair.Key)) continue;
                if (!Guid.TryParse(pair.Value.Value, out Guid fileId)) continue;
                try { await Client.DeleteFileAsync(fileId, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log($"{kind} / {key}: не удалось удалить лишнее фото {fileId}: {ex.Message}", 2); }
            }

            if (last == null || !last.Changed) return null;

            // Фото, которые правка из файла заменила или убрала (вместе с пунктом), — больше ни на что не ссылаются.
            foreach (string replaced in last.ReplacedFileIds)
            {
                if (!Guid.TryParse(replaced, out Guid fileId)) continue;
                try { await Client.DeleteFileAsync(fileId, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log($"{kind} / {key}: не удалось удалить заменённое фото {fileId}: {ex.Message}", 2); }
            }

            if (kind == DocumentKinds.AutoCheck && last.TakenNumbers.Count > 0)
            {
                try { await PostShareLogsAsync(key, last.TakenNumbers, ct).ConfigureAwait(false); }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log($"{kind} / {key}: не удалось перенести логи автопроверок — {ex.Message}", 2);
                }
            }
            Log($"{kind} / {key}: правки из файла применены, v{apiDoc.Version} → v{merged.Version}", 1);
            return merged;
        }

        /// <summary>
        /// Фото пунктов, у которых в файле появилось или сменилось фото и которого нет в API:
        /// файл с шары ({модель},checklist_photos\{id}\{фото}) → /api/files. id пункта → (фото, id файла).
        /// </summary>
        private async Task<Dictionary<string, KeyValuePair<string, string>>> UploadFilePhotosAsync(string key, StoredDocument apiDoc,
            JToken baseToken, JToken theirs, CancellationToken ct)
        {
            var result = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            var baseById = ById(baseToken);
            var oursById = ById(string.IsNullOrEmpty(apiDoc.Json) ? null : ApiDocumentStore.ParseVerbatim(apiDoc.Json));
            var share = new ChecklistAttachments(_files, key);
            var local = new ChecklistAttachments(this, key);

            foreach (JObject t in (theirs as JArray ?? new JArray()).OfType<JObject>())
            {
                string idText = t.Value<string>("id");
                string photo = t.Value<string>(FileSyncMerge.PhotoProperty);
                if (string.IsNullOrEmpty(photo) || !Guid.TryParse(idText, out Guid id)) continue;
                if (baseById.TryGetValue(idText, out JObject b) && b.Value<string>(FileSyncMerge.PhotoProperty) == photo) continue;
                if (oursById.TryGetValue(idText, out JObject o) && o.Value<string>(FileSyncMerge.PhotoProperty) == photo
                    && !string.IsNullOrEmpty(o.Value<string>(FileSyncMerge.PhotoFileIdProperty)))
                    continue; // уже в API (синхронизировал другой)

                string path = SafePaths.PhotoPath(share.PhotosRoot, id, photo);
                if (path == null) continue;
                try
                {
                    if (!File.Exists(path))
                    {
                        Log($"checklist / {key}: нет файла фото {idText}\\{photo} на шаре", 2);
                        continue;
                    }
                    // Копия в локальный кэш — окно покажет фото без скачивания.
                    string cached = SafePaths.PhotoPath(local.PhotosRoot, id, photo);
                    if (cached != null && !File.Exists(cached))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cached));
                        File.Copy(path, cached, true);
                    }
                    string fileId = await local.UploadPhotoAsync(id, cached != null && File.Exists(cached) ? cached : path, ct).ConfigureAwait(false);
                    result[idText] = new KeyValuePair<string, string>(photo, fileId);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log($"checklist / {key}: не удалось загрузить фото {idText}\\{photo}: {ex.Message}", 2);
                }
            }
            return result;
        }

        /// <summary>Логи {модель},autocheck_checklogs\{n}.txt с шары → события autocheck_log (source = "filesync").</summary>
        private async Task PostShareLogsAsync(string key, IEnumerable<int> numbers, CancellationToken ct)
        {
            string shareRoot = new ChecklistAttachments(_files, key).LogsRoot;
            var events = new List<TNovEvent>();
            foreach (int number in numbers.Distinct())
            {
                string path = Path.Combine(shareRoot, number.ToString(CultureInfo.InvariantCulture) + ".txt");
                if (!File.Exists(path)) continue;
                var ts = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                string text = File.ReadAllText(path);

                TNovEvent latest = await Client.GetLatestEventAsync(ChecklistAttachments.LogEventKind, key, number, ct).ConfigureAwait(false);
                if (latest?.Extra != null && latest.Extra.Value<string>("text") == text) continue; // уже есть

                events.Add(new TNovEvent
                {
                    Kind = ChecklistAttachments.LogEventKind,
                    Ts = ts,
                    User = Environment.UserName,
                    Machine = Environment.MachineName,
                    Doc = key,
                    Command = "autocheck",
                    Extra = new JObject { ["file"] = number + ".txt", ["number"] = number, ["text"] = text, ["source"] = "filesync" }
                });
            }
            if (events.Count > 0)
                await Client.PostEventsAsync(events, ct).ConfigureAwait(false);
        }

        private static Dictionary<string, JObject> ById(JToken token)
        {
            var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject o in (token as JArray ?? new JArray()).OfType<JObject>())
            {
                string id = o.Value<string>("id");
                if (!string.IsNullOrEmpty(id) && !map.ContainsKey(id)) map[id] = o;
            }
            return map;
        }

        // ================================================================ состояние

        private struct StateLoad
        {
            public bool Ok;
            public FileSyncState State;
            public long Version;
        }

        private async Task<StateLoad> TryLoadStateAsync(string kind, string key, CancellationToken ct)
        {
            try
            {
                TNovDocument<JToken> doc = await Client.GetDocumentAsync<JToken>(StateKindPrefix + kind, key, ct).ConfigureAwait(false);
                if (doc == null) return new StateLoad { Ok = true };
                FileSyncState state = FileSyncState.FromJson(doc.Data as JObject);
                if (state != null) state.DocVersion = doc.Version;
                return new StateLoad { Ok = true, State = state, Version = doc.Version };
            }
            catch (Exception ex)
            {
                Log($"{kind} / {key}: не удалось прочитать состояние синхронизации — {ex.Message}", 2);
                return new StateLoad { Ok = false };
            }
        }

        /// <summary>
        /// Сохранить состояние. Конфликт (записал другой) — побеждает состояние с более новой отметкой файла.
        /// </summary>
        private async Task SaveStateAsync(string kind, string key, FileSyncState state, long expectedVersion, CancellationToken ct)
        {
            state.SyncedAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            state.SyncedBy = Environment.UserName;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                SaveResult<JToken> saved = await Client.SaveDocumentAsync<JToken>(StateKindPrefix + kind, key, state.ToJson(),
                    expectedVersion == 0 ? (long?)null : expectedVersion, ct).ConfigureAwait(false);
                if (saved.Saved) return;
                if (saved.Current == null) { expectedVersion = 0; continue; }
                FileSyncState other = FileSyncState.FromJson(saved.Current.Data as JObject);
                if (other != null && other.FileStamp > state.FileStamp) return;
                if (other != null && other.FileStamp == state.FileStamp && other.WrittenVersion >= state.WrittenVersion) return;
                expectedVersion = saved.Current.Version;
            }
            Log($"{kind} / {key}: состояние синхронизации не сохранено — постоянно меняется", 2);
        }

        // ================================================================ шара

        private struct FileStat
        {
            public bool Reachable;
            public bool Exists;
            public long Stamp;
        }

        private bool IsShareBackedOff() =>
            string.IsNullOrEmpty(_serverPath)
            || (_unreachableUntil.TryGetValue(_serverPath, out DateTime until) && DateTime.UtcNow < until);

        /// <summary>
        /// Одна отметка файла (один запрос атрибутов по SMB), не дольше <see cref="StatTimeout"/>:
        /// недоступная шара иначе держала бы каждое открытие до таймаута SMB.
        /// </summary>
        private async Task<FileStat> StatAsync(string kind, string key)
        {
            if (IsShareBackedOff()) return new FileStat();
            string path = _files.PathOf(kind, key);
            Task<FileStat> stat = Task.Run(() =>
            {
                var fi = new FileInfo(path);
                if (fi.Exists) return new FileStat { Reachable = true, Exists = true, Stamp = fi.LastWriteTimeUtc.Ticks };
                // FileInfo.Exists = false и при обрыве сети — «нет файла» только при доступной папке.
                bool reachable = Directory.Exists(Path.GetDirectoryName(path)) || Directory.Exists(_serverPath);
                return new FileStat { Reachable = reachable };
            });
            try
            {
                if (await Task.WhenAny(stat, Task.Delay(StatTimeout)).ConfigureAwait(false) == stat)
                {
                    FileStat result = await stat.ConfigureAwait(false);
                    if (!result.Reachable) MarkUnreachable("папка недоступна");
                    return result;
                }
                _ = stat.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                MarkUnreachable($"нет ответа за {StatTimeout.TotalSeconds:0} с");
            }
            catch (Exception ex)
            {
                MarkUnreachable(ex.Message);
            }
            return new FileStat();
        }

        private void MarkUnreachable(string why)
        {
            if (string.IsNullOrEmpty(_serverPath)) return;
            bool wasOk = !_unreachableUntil.TryGetValue(_serverPath, out DateTime until) || DateTime.UtcNow >= until;
            _unreachableUntil[_serverPath] = DateTime.UtcNow + UnreachableBackoff;
            if (wasOk) Log($"шара {_serverPath} недоступна ({why}) — синхронизация с файлами приостановлена на {UnreachableBackoff.TotalSeconds:0} с", 2);
        }

        private static SemaphoreSlim Gate(string kind, string key) =>
            _gates.GetOrAdd(kind + "\n" + key, _ => new SemaphoreSlim(1, 1));

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        private static void Log(string message, int level)
        {
            try { Logger.Log("FileSync: " + message, level); } catch (Exception) { }
            try { LogHook?.Invoke(message); } catch (Exception) { }
        }
    }

    /// <summary>Состояние синхронизации файла с API (документ filesync-{kind} / {модель}).</summary>
    internal sealed class FileSyncState
    {
        public long FileStamp { get; set; }
        public string FileHash { get; set; }
        public JToken BaseJson { get; set; }
        public long WrittenVersion { get; set; }
        /// <summary>UpdatedAt (тики UTC) записанной версии: после пересоздания документа версии начинаются заново.</summary>
        public long WrittenAt { get; set; }
        public string SyncedAt { get; set; }
        public string SyncedBy { get; set; }

        /// <summary>Версия документа состояния в API (не сериализуется).</summary>
        public long DocVersion { get; set; }

        public JObject ToJson() => new JObject
        {
            ["fileStamp"] = FileStamp,
            ["fileHash"] = FileHash,
            ["baseJson"] = BaseJson?.DeepClone() ?? JValue.CreateNull(),
            ["writtenVersion"] = WrittenVersion,
            ["writtenAt"] = WrittenAt,
            ["syncedAt"] = SyncedAt,
            ["syncedBy"] = SyncedBy
        };

        public static FileSyncState FromJson(JObject o)
        {
            if (o == null) return null;
            return new FileSyncState
            {
                FileStamp = o.Value<long?>("fileStamp") ?? 0,
                FileHash = o.Value<string>("fileHash"),
                BaseJson = o["baseJson"],
                WrittenVersion = o.Value<long?>("writtenVersion") ?? 0,
                WrittenAt = o.Value<long?>("writtenAt") ?? 0,
                SyncedAt = o.Value<string>("syncedAt"),
                SyncedBy = o.Value<string>("syncedBy")
            };
        }
    }
}
