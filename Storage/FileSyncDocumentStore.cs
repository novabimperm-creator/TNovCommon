using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TNovApi.Client;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Переходный период после перевода чек-листов в API ("ChecklistStorage": "api", "FileSync" ≠ false):
    /// основное хранилище — TNovApi, но файлы на шарах ({шара}projects\{модель},checklist.json …)
    /// поддерживаются в актуальном виде для пользователей, которые ещё не обновили плагин.
    ///
    /// Звезда: API — центр, каждая шара офиса ("FileSyncShares" в tnovapi.json; нет списка — одна
    /// шара ServerPath) — независимое ребро со своим трёхсторонним base. Одна модель открывается из
    /// обоих офисов (один Revit Server), и файлы на шарах разные: base одной шары к другой не применяется.
    ///
    ///  • Load: документ из API; своя шара (совпадает с ServerPath) — сразу, как раньше: одна отметка
    ///    времени файла, файл изменился со времени прошлой синхронизации ребра (правка старой версии) —
    ///    трёхстороннее слияние (<see cref="FileSyncMerge"/>) и запись результата в API; фото из файла
    ///    загружаются в /api/files, логи автопроверок уходят событиями autocheck_log.
    ///    Остальные шары — в фоне (<see cref="RemoteSyncTimeout"/> на шару, окно не ждёт): их правки
    ///    попадают в API новой версией, окно увидит её обычным опросом.
    ///  • Save: после удачного сохранения в API тот же документ пишется в файл своей шары (без
    ///    photo_file_id, Formatting.Indented, UTF-8 без BOM — как пишет старая версия), затем в фоне
    ///    на остальные шары. Если файл успели изменить — сначала слияние. Ошибка записи в файл
    ///    сохранение не отменяет: в лог, следующий Load сверит.
    ///  • Правки, пришедшие в API из файла одной шары, в фоне дописываются в файлы остальных.
    ///  • Poll и List — только API.
    ///
    /// Состояние синхронизации ребра хранится в API: документ filesync-{kind} / {модель}@{id шары}:
    /// { fileStamp (тики LastWriteTimeUtc), fileHash (sha256 текста), baseJson (содержимое файла
    ///   на момент синхронизации), writtenVersion / writtenAt (версия API, записанная в файл, и её UpdatedAt),
    ///   share, syncedAt, syncedBy }.
    /// Прежнее состояние без шары (filesync-{kind} / {модель}: TNovApi.Import и сборки до разделения
    /// по шарам) принимается ребром, только если оно описывает именно этот файл (<see cref="ResolveStateAsync"/>).
    ///
    /// Шара недоступна или отвечает дольше <see cref="StatTimeout"/> — работаем без неё и не трогаем
    /// её <see cref="UnreachableBackoff"/> (для каждой шары отдельно).
    /// </summary>
    public sealed class FileSyncDocumentStore : IDocumentStore, IApiDocumentStore
    {
        public const string StateKindPrefix = "filesync-";
        /// <summary>Разделитель модели и id шары в ключе состояния ('/' и '\' в ключах API запрещены).</summary>
        public const char StateKeySeparator = '@';

        internal static TimeSpan StatTimeout = TimeSpan.FromSeconds(5);
        internal static TimeSpan UnreachableBackoff = TimeSpan.FromSeconds(60);
        /// <summary>Сколько Save ждёт записи в файл своей шары; дольше — запись доделывается в фоне.</summary>
        internal static TimeSpan DualWriteWait = TimeSpan.FromSeconds(10);
        /// <summary>Предел фоновой синхронизации одной шары (API-запросы отменяются, SMB — по <see cref="StatTimeout"/>).</summary>
        internal static TimeSpan RemoteSyncTimeout = TimeSpan.FromSeconds(20);
        /// <summary>Для тестов: дублирует сообщения лога.</summary>
        internal static Action<string> LogHook = null;

        private const int WriteAttempts = 3;
        /// <summary>Сколько раз правка из одной шары может «отразиться» на другие (правки там → дальше).</summary>
        private const int MaxPropagation = 3;

        private static readonly Regex ShareIdPattern = new Regex("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _unreachableUntil =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _warned =
            new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private static int _background;

        /// <summary>Для тестов: сколько фоновых синхронизаций шар ещё идёт.</summary>
        internal static int BackgroundCount => Volatile.Read(ref _background);

        /// <summary>Ребро звезды: одна шара.</summary>
        internal sealed class Edge
        {
            public string Id;
            public string Path;
            public FileDocumentStore Files;
            /// <summary>Шара своего офиса (= ServerPath): синхронизируется сразу, остальные — в фоне.</summary>
            public bool Own;
        }

        private readonly ApiDocumentStore _api;
        private readonly Edge[] _edges;
        private readonly Edge _own;

        /// <param name="serverPath">ServerPath своего офиса.</param>
        /// <param name="shares">"FileSyncShares" из tnovapi.json; null/пусто — одна шара <paramref name="serverPath"/>.</param>
        public FileSyncDocumentStore(ApiDocumentStore api, string serverPath, IEnumerable<FileSyncShareConfig> shares = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _edges = BuildEdges(serverPath, shares);
            _own = _edges.FirstOrDefault(e => e.Own);
        }

        public string Name => "api";

        public TNovApiClient Client => _api.Client;

        public ApiDocumentStore Api => _api;

        /// <summary>Файлы своей шары (null — ServerPath нет в списке шар).</summary>
        public FileDocumentStore Files => _own?.Files;

        /// <summary>id шар (своя — первой, если есть).</summary>
        public IReadOnlyList<string> ShareIds => _edges.OrderBy(e => e.Own ? 0 : 1).Select(e => e.Id).ToList();

        private static bool IsSynced(string kind) => DocumentKinds.ChecklistKinds.Contains(kind);

        // ================================================================ шары

        /// <summary>Путь для сравнения: без пробелов по краям и слеша на конце, '\', строчные.</summary>
        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return path.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        /// <summary>id шары без списка в tnovapi.json: "p" + 8 hex sha1 нормализованного пути.</summary>
        public static string DerivedShareId(string path)
        {
            string normalized = NormalizePath(path) ?? "";
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder("p");
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Ключ состояния ребра: {модель}@{id шары}.</summary>
        public static string StateKey(string modelKey, string shareId) => modelKey + StateKeySeparator + shareId;

        private static string StateKey(string modelKey, Edge edge) => StateKey(modelKey, edge.Id);

        private static Edge[] BuildEdges(string serverPath, IEnumerable<FileSyncShareConfig> shares)
        {
            var list = new List<Edge>();
            string own = NormalizePath(serverPath);
            foreach (FileSyncShareConfig s in shares ?? Enumerable.Empty<FileSyncShareConfig>())
            {
                if (s == null) continue;
                string id = (s.Id ?? "").Trim().ToLowerInvariant();
                string norm = NormalizePath(s.Path);
                if (norm == null || !ShareIdPattern.IsMatch(id))
                {
                    WarnOnce($"FileSyncShares: шара «{s.Id}» ({s.Path}) пропущена — нужен id [a-z0-9-]{{1,32}} и путь");
                    continue;
                }
                if (list.Any(e => e.Id == id || NormalizePath(e.Path) == norm))
                {
                    WarnOnce($"FileSyncShares: повтор шары «{s.Id}» ({s.Path}) — пропущена");
                    continue;
                }
                bool isOwn = own != null && norm == own;
                string path = isOwn ? serverPath : s.Path.Trim();
                list.Add(new Edge { Id = id, Path = path, Files = new FileDocumentStore(path), Own = isOwn });
            }
            if (list.Count == 0 && own != null)
                list.Add(new Edge { Id = DerivedShareId(serverPath), Path = serverPath, Files = new FileDocumentStore(serverPath), Own = true });
            else if (own != null && !list.Any(e => e.Own))
                WarnOnce($"FileSyncShares: ServerPath {serverPath} нет в списке — все шары синхронизируются в фоне");
            return list.ToArray();
        }

        private static void WarnOnce(string message)
        {
            if (_warned.TryAdd(message, true)) Log(message, 1);
        }

        // ================================================================ IDocumentStore

        public async Task<StoredDocument> LoadAsync(string kind, string key, CancellationToken cancellationToken = default)
        {
            if (!IsSynced(kind)) return await _api.LoadAsync(kind, key, cancellationToken).ConfigureAwait(false);

            // Документ, состояние своей шары и отметка файла — параллельно: один round-trip к API и один запрос по SMB.
            Edge own = _own != null && !IsBackedOff(_own) ? _own : null;
            Task<StoredDocument> docTask = _api.LoadAsync(kind, key, cancellationToken);
            Task<StateLoad> stateTask = own != null ? TryLoadStateAsync(kind, StateKey(key, own), cancellationToken) : null;
            Task<FileStat> statTask = own != null ? StatAsync(own, kind, key) : null;

            StoredDocument doc = await docTask.ConfigureAwait(false);
            if (doc.FromCache) return doc; // API недоступен — сливать некуда

            bool apiChanged = false, catchUp = false;
            if (own != null)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    FileStat stat = await statTask.ConfigureAwait(false);
                    StateLoad state = await stateTask.ConfigureAwait(false);
                    if (stat.Reachable && stat.Exists && state.Ok)
                    {
                        state = await ResolveStateAsync(kind, key, own, state, stat, cancellationToken).ConfigureAwait(false);
                        if (state.Ok && state.State != null && state.State.FileStamp == stat.Stamp)
                        {
                            // Файл не менялся. В API новее, чем в файле (запись не дошла — шара была
                            // недоступна, сохранение без синхронизации), — дописать в фоне.
                            catchUp = doc.Exists
                                && !FileSyncMerge.Equivalent(kind, state.State.BaseJson, ApiDocumentStore.ParseVerbatim(doc.Json));
                        }
                        else if (state.Ok)
                        {
                            EdgeResult r = await SyncEdgeAsync(kind, key, own, doc, false, cancellationToken,
                                new Prefetch { State = state, Stat = stat }).ConfigureAwait(false);
                            if (r != null)
                            {
                                apiChanged = r.Merged;
                                doc = r.Latest ?? doc;
                            }
                        }
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Log($"{kind} / {key} @{own.Id}: синхронизация с файлом не удалась — {ex.GetType().Name}: {ex.Message}", 2);
                }
                Log($"{kind} / {key} @{own.Id}: открытие, своя шара {sw.ElapsedMilliseconds} мс", 2);
            }

            // Остальные шары (и дописывание своей) — в фоне. Слияние своей изменило API — это как сохранение:
            // файлы создаются и там, где их ещё нет.
            var background = _edges.Where(e => !e.Own || (catchUp && e == own)).ToList();
            if (background.Count > 0)
                SyncInBackground(kind, key, background, doc, apiChanged, 0, "открытие");
            return doc;
        }

        public Task<StoredDocument> PollAsync(string kind, string key, long knownVersion, CancellationToken cancellationToken = default) =>
            _api.PollAsync(kind, key, knownVersion, cancellationToken);

        public Task<IReadOnlyList<StoredDocumentInfo>> ListAsync(string kind, CancellationToken cancellationToken = default) =>
            _api.ListAsync(kind, cancellationToken);

        public async Task<SaveOutcome> SaveAsync(string kind, string key, string json, long expectedVersion, CancellationToken cancellationToken = default)
        {
            SaveOutcome outcome = await _api.SaveAsync(kind, key, json, expectedVersion, cancellationToken).ConfigureAwait(false);
            if (!outcome.Saved || !IsSynced(kind)) return outcome;

            StoredDocument saved = outcome.Current;
            List<Edge> remotes = _edges.Where(e => !e.Own).ToList();
            Edge own = _own;
            if (own == null || IsBackedOff(own))
            {
                if (remotes.Count > 0) SyncInBackground(kind, key, remotes, saved, true, 0, "сохранение");
                return outcome;
            }

            Task<EdgeResult> work = SyncEdgeAsync(kind, key, own, saved, true, CancellationToken.None, null);
            // Остальные шары — после своей: если своя дала слияние, туда уйдёт уже слитый документ.
            if (remotes.Count > 0)
                _ = work.ContinueWith(t => SyncInBackground(kind, key, remotes,
                        t.Status == TaskStatus.RanToCompletion && t.Result?.Latest != null ? t.Result.Latest : saved,
                        true, 0, "сохранение"),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            Task first = await Task.WhenAny(work, Task.Delay(DualWriteWait)).ConfigureAwait(false);
            if (first != work)
            {
                Log($"{kind} / {key} @{own.Id}: запись в файл идёт дольше {DualWriteWait.TotalSeconds:0} с — доделывается в фоне", 2);
                _ = work.ContinueWith(t => Log($"{kind} / {key} @{own.Id}: запись в файл не удалась — {t.Exception?.GetBaseException().Message}", 2),
                    TaskContinuationOptions.OnlyOnFaulted);
                return outcome;
            }
            try
            {
                EdgeResult r = await work.ConfigureAwait(false);
                // Слияние с файлом дало более новую версию в API — её и отдаём окну.
                if (r != null && r.Merged && r.Latest != null && r.Latest.Version > saved.Version)
                    return new SaveOutcome { Saved = true, Current = r.Latest };
            }
            catch (Exception ex)
            {
                Log($"{kind} / {key} @{own.Id}: запись в файл не удалась — {ex.GetType().Name}: {ex.Message}", 2);
            }
            return outcome;
        }

        // ================================================================ синхронизация ребра

        private sealed class EdgeResult
        {
            /// <summary>Последний известный документ API (после слияния, если оно было).</summary>
            public StoredDocument Latest;
            /// <summary>В API записаны правки из файла этой шары.</summary>
            public bool Merged;
        }

        /// <summary>Уже прочитанные (параллельно с документом) состояние и отметка — для первой попытки.</summary>
        private sealed class Prefetch
        {
            public StateLoad State;
            public FileStat Stat;
        }

        /// <summary>
        /// Фоновая синхронизация шар (fire-and-forget, не дольше <see cref="RemoteSyncTimeout"/> на шару).
        /// Правки из файла шары, попавшие в API, так же в фоне дописываются в файлы остальных шар.
        /// </summary>
        private void SyncInBackground(string kind, string key, IEnumerable<Edge> edges, StoredDocument latest, bool create,
            int depth, string reason)
        {
            foreach (Edge edge in edges)
            {
                if (IsBackedOff(edge)) continue;
                Edge e = edge;
                Interlocked.Increment(ref _background);
                Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using (var cts = new CancellationTokenSource(RemoteSyncTimeout))
                        {
                            try
                            {
                                EdgeResult r = await SyncEdgeAsync(kind, key, e, latest, create, cts.Token, null).ConfigureAwait(false);
                                bool merged = r != null && r.Merged;
                                Log($"{kind} / {key} @{e.Id}: {reason}, фоновая синхронизация {sw.ElapsedMilliseconds} мс"
                                    + (merged ? $", правки из файла → v{r.Latest.Version}" : ""), 2);
                                if (merged && depth + 1 < MaxPropagation)
                                    SyncInBackground(kind, key, _edges.Where(x => x != e), r.Latest, true, depth + 1, "правки из шары " + e.Id);
                            }
                            catch (OperationCanceledException) when (cts.IsCancellationRequested)
                            {
                                Log($"{kind} / {key} @{e.Id}: {reason}, фоновая синхронизация прервана через {sw.ElapsedMilliseconds} мс", 2);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"{kind} / {key} @{e.Id}: {reason}, фоновая синхронизация не удалась ({sw.ElapsedMilliseconds} мс) — {ex.GetType().Name}: {ex.Message}", 2);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _background);
                    }
                });
            }
        }

        /// <summary>
        /// Свести файл шары <paramref name="edge"/> с API: файл изменился со времени прошлой синхронизации
        /// ребра — слить правки в API; затем, если в файле не то, что в API, — записать (со слиянием,
        /// если файл успели изменить). <paramref name="create"/> = false — отсутствующий файл не создаётся.
        /// </summary>
        private async Task<EdgeResult> SyncEdgeAsync(string kind, string key, Edge edge, StoredDocument latest, bool create,
            CancellationToken ct, Prefetch pre)
        {
            SemaphoreSlim gate = Gate(kind, key, edge);
            bool contended = !gate.Wait(0);
            if (contended) await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (contended)
                {
                    // Пока ждали, эту шару синхронизировал другой (окно, фон, пакетный прогон) — всё перечитать.
                    pre = null;
                    StoredDocument fresh = await _api.LoadAsync(kind, key, ct).ConfigureAwait(false);
                    if (fresh.FromCache) return null;
                    latest = fresh;
                }

                var result = new EdgeResult { Latest = latest };
                string stateKey = StateKey(key, edge);
                for (int attempt = 0; attempt < WriteAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    StateLoad state;
                    FileStat stat;
                    if (pre != null && attempt == 0)
                    {
                        state = pre.State;
                        stat = pre.Stat;
                    }
                    else
                    {
                        state = await TryLoadStateAsync(kind, stateKey, ct).ConfigureAwait(false);
                        if (!state.Ok) return result;
                        stat = await StatAsync(edge, kind, key).ConfigureAwait(false);
                        if (!stat.Reachable) return result;
                        if (!stat.Exists && (!create || !result.Latest.Exists)) return result;
                        state = await ResolveStateAsync(kind, key, edge, state, stat, ct).ConfigureAwait(false);
                        if (!state.Ok) return result;
                    }

                    FileSyncState current = state.State;
                    long stateVersion = state.Version;
                    JToken fileBefore = current?.BaseJson;
                    long fileStamp = stat.Exists ? stat.Stamp : 0;
                    if (stat.Exists && (current == null || current.FileStamp != stat.Stamp))
                    {
                        // Файл изменили (старая версия) после нашей последней синхронизации — сначала забрать правку.
                        SyncResult sync = await SyncFromFileAsync(kind, key, edge, result.Latest, current, stat, ct).ConfigureAwait(false);
                        if (sync != null)
                        {
                            if (sync.Merged != null)
                            {
                                result.Latest = sync.Merged;
                                result.Merged = true;
                            }
                            current = sync.State;
                            stateVersion = sync.StateVersion;
                            fileBefore = sync.Theirs;
                            fileStamp = sync.State.FileStamp;
                        }
                    }

                    StoredDocument doc = result.Latest;
                    bool stateChanged = current != null && current != state.State;
                    if (!doc.Exists)
                    {
                        // В API документа нет, а файл нечего сливать (пустой / совпал) — только состояние.
                        if (stateChanged) await SaveStateAsync(kind, stateKey, edge, current, stateVersion, ct).ConfigureAwait(false);
                        return result;
                    }

                    if (current != null && current.WrittenVersion > doc.Version && current.WrittenAt != 0
                        && doc.UpdatedAtUtc.HasValue && current.WrittenAt >= doc.UpdatedAtUtc.Value.Ticks)
                    {
                        // В файле уже более новая версия (записал другой пользователь).
                        if (stateChanged) await SaveStateAsync(kind, stateKey, edge, current, stateVersion, ct).ConfigureAwait(false);
                        return result;
                    }

                    JToken data = ApiDocumentStore.ParseVerbatim(doc.Json);
                    if (fileStamp != 0 && fileBefore != null && current != null && current.FileStamp == fileStamp
                        && FileSyncMerge.Equivalent(kind, fileBefore, data))
                    {
                        // Содержимое для старых версий не изменилось (например, только photo_file_id).
                        if (current.WrittenVersion != doc.Version || stateChanged)
                        {
                            current.WrittenVersion = doc.Version;
                            current.WrittenAt = doc.UpdatedAtUtc?.Ticks ?? 0;
                            await SaveStateAsync(kind, stateKey, edge, current, stateVersion, ct).ConfigureAwait(false);
                        }
                        return result;
                    }

                    FileSyncState written = await TryWriteFileAsync(kind, key, edge, doc, fileBefore, fileStamp, ct).ConfigureAwait(false);
                    if (written != null)
                    {
                        await SaveStateAsync(kind, stateKey, edge, written, stateVersion, ct).ConfigureAwait(false);
                        return result;
                    }
                    // Файл изменился между проверкой и записью — ещё раз со слиянием.
                    if (stateChanged)
                        await SaveStateAsync(kind, stateKey, edge, current, stateVersion, ct).ConfigureAwait(false);
                }
                Log($"{kind} / {key} @{edge.Id}: файл постоянно меняется — не записан, сверится при следующем открытии", 2);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Записать документ API в файл шары, если файл всё ещё с отметкой <paramref name="expectedStamp"/>
        /// (0 — файла нет). Сначала вложения (фото, логи), потом JSON — старая версия, увидев новый
        /// пункт, сразу найдёт его фото. Возвращает новое состояние или null (файл успели изменить).
        /// </summary>
        private async Task<FileSyncState> TryWriteFileAsync(string kind, string key, Edge edge, StoredDocument doc, JToken fileBefore,
            long expectedStamp, CancellationToken ct)
        {
            JToken data = ApiDocumentStore.ParseVerbatim(doc.Json);
            try
            {
                await CopyAttachmentsToShareAsync(kind, key, edge, data, fileBefore, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log($"{kind} / {key} @{edge.Id}: не удалось скопировать вложения на шару — {ex.Message}", 2);
            }

            JToken fileData = FileSyncMerge.StripPhotoIds(data);
            string text = FileSyncMerge.Serialize(fileData);
            SaveOutcome outcome = await edge.Files.SaveAsync(kind, key, text, expectedStamp, ct).ConfigureAwait(false);
            if (!outcome.Saved) return null;
            Log($"{kind} / {key} @{edge.Id}: v{doc.Version} записана в файл", 2);
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
        private async Task CopyAttachmentsToShareAsync(string kind, string key, Edge edge, JToken data, JToken fileBefore, CancellationToken ct)
        {
            var local = new ChecklistAttachments(this, key);
            var share = new ChecklistAttachments(edge.Files, key);
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
        private async Task<SyncResult> SyncFromFileAsync(string kind, string key, Edge edge, StoredDocument apiDoc, FileSyncState state,
            FileStat stat, CancellationToken ct)
        {
            long stateVersion = state?.DocVersion ?? 0;
            StoredDocument file = await edge.Files.LoadAsync(kind, key, ct).ConfigureAwait(false);
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
                // Первая синхронизация этой шары (импорт не записал состояние, прежнее состояние — от другого файла).
                if (FileSyncMerge.Equivalent(kind, theirs, ours))
                {
                    newState.WrittenVersion = apiDoc.Version;
                    newState.WrittenAt = apiDoc.UpdatedAtUtc?.Ticks ?? 0;
                    return result;
                }
                DateTime fileTime = new DateTime(file.Version, DateTimeKind.Utc);
                if (apiDoc.Exists && apiDoc.UpdatedAtUtc.HasValue && apiDoc.UpdatedAtUtc.Value >= fileTime)
                {
                    Log($"{kind} / {key} @{edge.Id}: состояния нет, документ в API новее файла — правки файла не применяются", 2);
                    return result;
                }
                // Файл новее — его новые пункты и отличающиеся свойства считаем правками файла. Пункты API,
                // которых в файле нет, не удаляются: неизвестно, удалены ли они или файл просто другой.
                Log($"{kind} / {key} @{edge.Id}: состояния нет, файл новее документа в API — отличия файла применяются (без удалений)", 2);
                baseToken = FileSyncMerge.NoStateBase(kind, ours, theirs);
            }

            result.Merged = await ApplyFileEditsAsync(kind, key, edge, apiDoc, baseToken, theirs, ct).ConfigureAwait(false);
            return result;
        }

        private async Task<StoredDocument> ApplyFileEditsAsync(string kind, string key, Edge edge, StoredDocument apiDoc,
            JToken baseToken, JToken theirs, CancellationToken ct)
        {
            // Фото: загрузить до слияния (mutate синхронный и может повторяться).
            var photoIds = kind == DocumentKinds.Checklist
                ? await UploadFilePhotosAsync(key, edge, apiDoc, baseToken, theirs, ct).ConfigureAwait(false)
                : new Dictionary<string, KeyValuePair<string, string>>();

            // Через UpdateAsync: при конфликте версий слияние применяется к свежему документу — так
            // одновременные синхронизации разных шар (и сохранения окон) складываются, а не затирают друг друга.
            FileSyncMerge.Result last = null;
            StoredDocument merged = await _api.UpdateAsync(kind, key, json =>
            {
                JToken ours = string.IsNullOrEmpty(json) ? new JArray() : ApiDocumentStore.ParseVerbatim(json);
                last = FileSyncMerge.Apply(kind, baseToken, theirs, ours, photoIds);
                return last.Changed ? FileSyncMerge.Serialize(last.Merged) : null;
            }, apiDoc, cancellationToken: ct).ConfigureAwait(false);

            if (last != null)
                foreach (string note in last.Notes)
                    Log($"{kind} / {key} @{edge.Id}: {note}", 2);

            // Загруженные, но не понадобившиеся фото (победил API) — удалить.
            foreach (var pair in photoIds)
            {
                if (last != null && last.Changed && last.PhotosApplied.Contains(pair.Key)) continue;
                if (!Guid.TryParse(pair.Value.Value, out Guid fileId)) continue;
                try { await Client.DeleteFileAsync(fileId, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log($"{kind} / {key} @{edge.Id}: не удалось удалить лишнее фото {fileId}: {ex.Message}", 2); }
            }

            if (last == null || !last.Changed) return null;

            // Фото, которые правка из файла заменила или убрала (вместе с пунктом), — больше ни на что не ссылаются.
            foreach (string replaced in last.ReplacedFileIds)
            {
                if (!Guid.TryParse(replaced, out Guid fileId)) continue;
                try { await Client.DeleteFileAsync(fileId, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log($"{kind} / {key} @{edge.Id}: не удалось удалить заменённое фото {fileId}: {ex.Message}", 2); }
            }

            if (kind == DocumentKinds.AutoCheck && last.TakenNumbers.Count > 0)
            {
                try { await PostShareLogsAsync(key, edge, last.TakenNumbers, ct).ConfigureAwait(false); }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log($"{kind} / {key} @{edge.Id}: не удалось перенести логи автопроверок — {ex.Message}", 2);
                }
            }
            Log($"{kind} / {key} @{edge.Id}: правки из файла применены, v{apiDoc.Version} → v{merged.Version}", 1);
            return merged;
        }

        /// <summary>
        /// Фото пунктов, у которых в файле появилось или сменилось фото и которого нет в API:
        /// файл с шары ({модель},checklist_photos\{id}\{фото}) → /api/files. id пункта → (фото, id файла).
        /// </summary>
        private async Task<Dictionary<string, KeyValuePair<string, string>>> UploadFilePhotosAsync(string key, Edge edge, StoredDocument apiDoc,
            JToken baseToken, JToken theirs, CancellationToken ct)
        {
            var result = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            var baseById = ById(baseToken);
            var oursById = ById(string.IsNullOrEmpty(apiDoc.Json) ? null : ApiDocumentStore.ParseVerbatim(apiDoc.Json));
            var share = new ChecklistAttachments(edge.Files, key);
            var local = new ChecklistAttachments(this, key);

            foreach (JObject t in (theirs as JArray ?? new JArray()).OfType<JObject>())
            {
                string idText = t.Value<string>("id");
                string photo = t.Value<string>(FileSyncMerge.PhotoProperty);
                if (string.IsNullOrEmpty(photo) || !Guid.TryParse(idText, out Guid id)) continue;
                bool inOurs = oursById.TryGetValue(idText, out JObject o);
                if (inOurs && o.Value<string>(FileSyncMerge.PhotoProperty) == photo
                    && !string.IsNullOrEmpty(o.Value<string>(FileSyncMerge.PhotoFileIdProperty)))
                    continue; // уже в API (синхронизировал другой)
                if (baseById.TryGetValue(idText, out JObject b) && b.Value<string>(FileSyncMerge.PhotoProperty) == photo)
                {
                    // Фото в файле не меняли. Нужно, только если пункт вернётся (изменён в файле, удалён в API).
                    if (inOurs || FileSyncMerge.ItemEquals(b, t, strict: false)) continue;
                }

                string path = SafePaths.PhotoPath(share.PhotosRoot, id, photo);
                if (path == null) continue;
                try
                {
                    if (!File.Exists(path))
                    {
                        Log($"checklist / {key} @{edge.Id}: нет файла фото {idText}\\{photo} на шаре", 2);
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
                    Log($"checklist / {key} @{edge.Id}: не удалось загрузить фото {idText}\\{photo}: {ex.Message}", 2);
                }
            }
            return result;
        }

        /// <summary>Логи {модель},autocheck_checklogs\{n}.txt с шары → события autocheck_log (source = "filesync").</summary>
        private async Task PostShareLogsAsync(string key, Edge edge, IEnumerable<int> numbers, CancellationToken ct)
        {
            string shareRoot = new ChecklistAttachments(edge.Files, key).LogsRoot;
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
                    Extra = new JObject { ["file"] = number + ".txt", ["number"] = number, ["text"] = text, ["source"] = "filesync", ["share"] = edge.Id }
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

        private async Task<StateLoad> TryLoadStateAsync(string kind, string stateKey, CancellationToken ct)
        {
            try
            {
                TNovDocument<JToken> doc = await Client.GetDocumentAsync<JToken>(StateKindPrefix + kind, stateKey, ct).ConfigureAwait(false);
                if (doc == null) return new StateLoad { Ok = true };
                FileSyncState state = FileSyncState.FromJson(doc.Data as JObject);
                if (state != null) state.DocVersion = doc.Version;
                return new StateLoad { Ok = true, State = state, Version = doc.Version };
            }
            catch (Exception ex)
            {
                Log($"{kind} / {stateKey}: не удалось прочитать состояние синхронизации — {ex.Message}", 2);
                return new StateLoad { Ok = false };
            }
        }

        /// <summary>
        /// Состояния ребра {модель}@{id} ещё нет — принять прежнее, если оно про этот же файл:
        ///  1) {модель}@{производный id} той же папки (до появления "FileSyncShares" шара называлась так) — всегда;
        ///  2) {модель} (TNovApi.Import, сборки до разделения по шарам; писалось с одной, чаще пермской,
        ///     шары) — только если его отметка совпадает с отметкой файла этой шары, либо его fileHash —
        ///     с sha256 текста файла, либо его baseJson совпадает с файлом по смыслу.
        /// Принятое сразу записывается под новым ключом; прежний документ не трогается.
        /// Иначе — «состояния нет» (правило первой синхронизации: отметка файла против UpdatedAt в API).
        /// </summary>
        private async Task<StateLoad> ResolveStateAsync(string kind, string key, Edge edge, StateLoad loaded, FileStat stat, CancellationToken ct)
        {
            if (!loaded.Ok || loaded.State != null) return loaded;

            string derived = DerivedShareId(edge.Path);
            if (derived != edge.Id)
            {
                StateLoad d = await TryLoadStateAsync(kind, StateKey(key, derived), ct).ConfigureAwait(false);
                if (!d.Ok) return d;
                if (d.State != null) return await AdoptAsync(kind, key, edge, d.State, "состояние той же шары (" + derived + ")", ct).ConfigureAwait(false);
            }

            if (!stat.Exists) return loaded;
            StateLoad legacy = await TryLoadStateAsync(kind, key, ct).ConfigureAwait(false);
            if (!legacy.Ok) return legacy;
            if (legacy.State == null) return loaded;

            string why = null;
            if (legacy.State.FileStamp == stat.Stamp) why = "совпала отметка файла";
            else
            {
                StoredDocument file = await edge.Files.LoadAsync(kind, key, ct).ConfigureAwait(false);
                if (file.Exists)
                {
                    if (!string.IsNullOrEmpty(legacy.State.FileHash) && FileSyncMerge.Hash(file.Json) == legacy.State.FileHash)
                        why = "совпал хэш файла";
                    else if (legacy.State.BaseJson != null && legacy.State.BaseJson.Type == JTokenType.Array
                        && FileSyncMerge.Equivalent(kind, legacy.State.BaseJson, ApiDocumentStore.ParseVerbatim(file.Json)))
                        why = "base совпал с файлом";
                }
            }
            if (why == null)
            {
                Log($"{kind} / {key} @{edge.Id}: прежнее состояние {{модель}} — от другого файла, не принято", 2);
                return loaded;
            }
            return await AdoptAsync(kind, key, edge, legacy.State, "прежнее состояние {модель}, " + why, ct).ConfigureAwait(false);
        }

        private async Task<StateLoad> AdoptAsync(string kind, string key, Edge edge, FileSyncState state, string why, CancellationToken ct)
        {
            string stateKey = StateKey(key, edge);
            state.DocVersion = 0;
            long version = await SaveStateAsync(kind, stateKey, edge, state, 0, ct).ConfigureAwait(false);
            Log($"{kind} / {key} @{edge.Id}: принято {why}", 2);
            if (version != 0)
            {
                state.DocVersion = version;
                return new StateLoad { Ok = true, State = state, Version = version };
            }
            // Одновременно записал другой — берём его.
            return await TryLoadStateAsync(kind, stateKey, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Сохранить состояние ребра (If-Match по версии документа состояния; 0 — только создать).
        /// Конфликт (записал другой) — побеждает состояние с более новой отметкой файла.
        /// Возвращает новую версию документа состояния или 0 (оставлено чужое).
        /// </summary>
        private async Task<long> SaveStateAsync(string kind, string stateKey, Edge edge, FileSyncState state, long expectedVersion, CancellationToken ct)
        {
            state.Share = edge.Id;
            state.SyncedAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            state.SyncedBy = Environment.UserName;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                SaveResult<JToken> saved = await Client.SaveDocumentAsync<JToken>(StateKindPrefix + kind, stateKey, state.ToJson(),
                    expectedVersion == 0 ? (long?)null : expectedVersion, ct).ConfigureAwait(false);
                if (saved.Saved) return saved.Current?.Version ?? 0;
                if (saved.Current == null) { expectedVersion = 0; continue; }
                FileSyncState other = FileSyncState.FromJson(saved.Current.Data as JObject);
                if (other != null && other.FileStamp > state.FileStamp) return 0;
                if (other != null && other.FileStamp == state.FileStamp && other.WrittenVersion >= state.WrittenVersion) return 0;
                expectedVersion = saved.Current.Version;
            }
            Log($"{kind} / {stateKey}: состояние синхронизации не сохранено — постоянно меняется", 2);
            return 0;
        }

        // ================================================================ шара

        private struct FileStat
        {
            public bool Reachable;
            public bool Exists;
            public long Stamp;
        }

        private static bool IsBackedOff(Edge edge) =>
            string.IsNullOrEmpty(edge.Path)
            || (_unreachableUntil.TryGetValue(NormalizePath(edge.Path), out DateTime until) && DateTime.UtcNow < until);

        /// <summary>
        /// Одна отметка файла (один запрос атрибутов по SMB), не дольше <see cref="StatTimeout"/>:
        /// недоступная шара иначе держала бы каждое открытие до таймаута SMB.
        /// </summary>
        private static async Task<FileStat> StatAsync(Edge edge, string kind, string key)
        {
            if (IsBackedOff(edge)) return new FileStat();
            string path = edge.Files.PathOf(kind, key);
            Task<FileStat> stat = Task.Run(() =>
            {
                var fi = new FileInfo(path);
                if (fi.Exists) return new FileStat { Reachable = true, Exists = true, Stamp = fi.LastWriteTimeUtc.Ticks };
                // FileInfo.Exists = false и при обрыве сети — «нет файла» только при доступной папке.
                bool reachable = Directory.Exists(Path.GetDirectoryName(path)) || Directory.Exists(edge.Path);
                return new FileStat { Reachable = reachable };
            });
            try
            {
                if (await Task.WhenAny(stat, Task.Delay(StatTimeout)).ConfigureAwait(false) == stat)
                {
                    FileStat result = await stat.ConfigureAwait(false);
                    if (!result.Reachable) MarkUnreachable(edge, "папка недоступна");
                    return result;
                }
                _ = stat.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                MarkUnreachable(edge, $"нет ответа за {StatTimeout.TotalSeconds:0} с");
            }
            catch (Exception ex)
            {
                MarkUnreachable(edge, ex.Message);
            }
            return new FileStat();
        }

        private static void MarkUnreachable(Edge edge, string why)
        {
            string norm = NormalizePath(edge.Path);
            if (norm == null) return;
            bool wasOk = !_unreachableUntil.TryGetValue(norm, out DateTime until) || DateTime.UtcNow >= until;
            _unreachableUntil[norm] = DateTime.UtcNow + UnreachableBackoff;
            if (wasOk) Log($"шара {edge.Id} ({edge.Path}) недоступна ({why}) — синхронизация с её файлами приостановлена на {UnreachableBackoff.TotalSeconds:0} с", 2);
        }

        private static SemaphoreSlim Gate(string kind, string key, Edge edge) =>
            _gates.GetOrAdd(kind + "\n" + key + "\n" + edge.Id, _ => new SemaphoreSlim(1, 1));

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

    /// <summary>Состояние синхронизации файла шары с API (документ filesync-{kind} / {модель}@{id шары}).</summary>
    internal sealed class FileSyncState
    {
        public long FileStamp { get; set; }
        public string FileHash { get; set; }
        public JToken BaseJson { get; set; }
        public long WrittenVersion { get; set; }
        /// <summary>UpdatedAt (тики UTC) записанной версии: после пересоздания документа версии начинаются заново.</summary>
        public long WrittenAt { get; set; }
        /// <summary>id шары (для диагностики; ключ документа — главный).</summary>
        public string Share { get; set; }
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
            ["share"] = Share,
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
                Share = o.Value<string>("share"),
                SyncedAt = o.Value<string>("syncedAt"),
                SyncedBy = o.Value<string>("syncedBy")
            };
        }
    }
}
