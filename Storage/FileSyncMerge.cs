using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TNovCommon.Storage
{
    /// <summary>
    /// Трёхстороннее слияние документов чек-листа на JToken (формат JSON не меняется):
    /// base — файл на шаре на момент прошлой синхронизации, theirs — файл сейчас (правки
    /// старых версий плагина), ours — свежий документ в API. Применяются только правки,
    /// сделанные в файле (theirs ≠ base), и только там, где API их не перебил.
    ///
    ///   checklist (пункты по "id"): новые в файле — добавить, если в API такого id нет;
    ///     удалённые в файле — удалить из API, только если пункт в API равен base;
    ///     свойства — по одному: применить, если в API значение всё ещё как в base;
    ///     изменены с обеих сторон по-разному — побеждает API (в лог).
    ///   autocheck (по "Number") и bimcheck (по "id"): пункт, изменённый в файле, берётся,
    ///     если в API его нет или "created_at" в файле новее. Пустая заготовка автопроверки
    ///     (без title) не перебивает настоящий результат. Удалений нет.
    ///
    /// Отсутствующее в пункте файла свойство — «старая версия его не знает», а не удаление:
    /// оно не трогается. photo_file_id в файле не бывает (старый CheckItem его не пишет) и
    /// при сравнении не учитывается. Даты ("…T…+05:00") сравниваются как моменты времени:
    /// старая версия в другом часовом поясе перепишет смещение, но это не правка.
    /// Все функции чистые (повторяются при конфликте версий API).
    /// </summary>
    internal static class FileSyncMerge
    {
        public const string PhotoProperty = "photo";
        public const string PhotoFileIdProperty = "photo_file_id";
        public const string CreatedAtProperty = "created_at";

        private static readonly Regex IsoDate = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", RegexOptions.CultureInvariant);

        public sealed class Result
        {
            public JArray Merged { get; set; }
            public bool Changed { get; set; }
            public List<string> Notes { get; } = new List<string>();
            /// <summary>autocheck: номера пунктов, взятых из файла (для переноса логов).</summary>
            public List<int> TakenNumbers { get; } = new List<int>();
            /// <summary>checklist: id пунктов, которым выставлено фото из файла.</summary>
            public HashSet<string> PhotosApplied { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>checklist: photo_file_id фото, которые заменены или убраны правкой из файла (удалить после сохранения).</summary>
            public List<string> ReplacedFileIds { get; } = new List<string>();
        }

        /// <param name="photoIds">checklist: id пункта → (имя фото, photo_file_id загруженного файла).</param>
        public static Result Apply(string kind, JToken baseToken, JToken theirs, JToken ours,
            IReadOnlyDictionary<string, KeyValuePair<string, string>> photoIds = null)
        {
            var result = new Result { Merged = ours is JArray a ? (JArray)a.DeepClone() : new JArray() };
            JArray baseArr = baseToken as JArray ?? new JArray();
            JArray theirsArr = theirs as JArray ?? new JArray();
            switch (kind)
            {
                case DocumentKinds.Checklist:
                    MergeChecklist(baseArr, theirsArr, result, photoIds);
                    break;
                case DocumentKinds.AutoCheck:
                    MergeNewerWins(baseArr, theirsArr, result, "Number", placeholderGuard: true);
                    break;
                case DocumentKinds.BimCheck:
                    MergeNewerWins(baseArr, theirsArr, result, "id", placeholderGuard: false);
                    break;
            }
            return result;
        }

        // ---------------- checklist ----------------

        private static void MergeChecklist(JArray baseArr, JArray theirsArr, Result r,
            IReadOnlyDictionary<string, KeyValuePair<string, string>> photoIds)
        {
            JArray ours = r.Merged;
            var baseById = Index(baseArr, "id");
            var theirsById = Index(theirsArr, "id");
            var oursById = Index(ours, "id");

            foreach (JObject t in theirsArr.OfType<JObject>())
            {
                string id = KeyOf(t, "id");
                if (id == null) continue;
                baseById.TryGetValue(id, out JObject b);
                oursById.TryGetValue(id, out JObject o);

                if (b == null)
                {
                    if (o != null) continue; // пункт с тем же id уже есть в API — API главнее
                    var added = (JObject)t.DeepClone();
                    added.Remove(PhotoFileIdProperty);
                    SetPhotoFileId(added, id, photoIds, r);
                    ours.Add(added);
                    oursById[id] = added;
                    r.Changed = true;
                    r.Notes.Add($"добавлен пункт {id}");
                    continue;
                }

                if (ItemEquals(b, t, strict: false)) continue; // в файле пункт не меняли
                if (o == null)
                {
                    r.Notes.Add($"конфликт: пункт {id} изменён в файле, но удалён в API — остаётся удалённым");
                    continue;
                }

                foreach (JProperty p in t.Properties().ToList())
                {
                    if (p.Name == PhotoFileIdProperty) continue;
                    JToken tv = p.Value;
                    JToken bv = b[p.Name];
                    if (ValueEquals(bv, tv)) continue;
                    JToken ov = o[p.Name];
                    if (ValueEquals(ov, tv)) continue; // уже так же
                    if (ValueEquals(ov, bv))
                    {
                        o[p.Name] = tv.DeepClone();
                        if (p.Name == PhotoProperty)
                        {
                            string replaced = o.Value<string>(PhotoFileIdProperty);
                            if (!string.IsNullOrEmpty(replaced)) r.ReplacedFileIds.Add(replaced);
                            o.Remove(PhotoFileIdProperty);
                            SetPhotoFileId(o, id, photoIds, r);
                        }
                        r.Changed = true;
                        r.Notes.Add($"пункт {id}: {p.Name} из файла");
                    }
                    else
                    {
                        r.Notes.Add($"конфликт: пункт {id}, {p.Name} изменён и в файле ({Short(tv)}), и в API ({Short(ov)}) — остаётся значение API");
                    }
                }
            }

            foreach (JObject b in baseArr.OfType<JObject>())
            {
                string id = KeyOf(b, "id");
                if (id == null || theirsById.ContainsKey(id)) continue;
                if (!oursById.TryGetValue(id, out JObject o)) continue; // удалён с обеих сторон
                if (ItemEquals(o, b))
                {
                    string fileId = o.Value<string>(PhotoFileIdProperty);
                    if (!string.IsNullOrEmpty(fileId)) r.ReplacedFileIds.Add(fileId);
                    ours.Remove(o);
                    oursById.Remove(id);
                    r.Changed = true;
                    r.Notes.Add($"удалён пункт {id}");
                }
                else
                {
                    r.Notes.Add($"конфликт: пункт {id} удалён в файле, но изменён в API — остаётся");
                }
            }
        }

        private static void SetPhotoFileId(JObject item, string id, IReadOnlyDictionary<string, KeyValuePair<string, string>> photoIds, Result r)
        {
            string photo = item.Value<string>(PhotoProperty);
            if (string.IsNullOrEmpty(photo)) return;
            r.PhotosApplied.Add(id);
            if (photoIds != null && photoIds.TryGetValue(id, out var up) && up.Key == photo && !string.IsNullOrEmpty(up.Value))
                item[PhotoFileIdProperty] = up.Value;
        }

        // ---------------- autocheck / bimcheck ----------------

        private static void MergeNewerWins(JArray baseArr, JArray theirsArr, Result r, string keyName, bool placeholderGuard)
        {
            JArray ours = r.Merged;
            var baseByKey = Index(baseArr, keyName);
            var oursByKey = Index(ours, keyName);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (JObject t in theirsArr.OfType<JObject>())
            {
                string key = KeyOf(t, keyName);
                if (key == null || !seen.Add(key)) continue;
                if (baseByKey.TryGetValue(key, out JObject b) && ItemEquals(b, t, strict: false)) continue; // в файле не меняли

                if (!oursByKey.TryGetValue(key, out JObject o))
                {
                    var added = (JObject)t.DeepClone();
                    ours.Add(added);
                    oursByKey[key] = added;
                    r.Changed = true;
                    Taken(r, t, keyName, placeholderGuard);
                    r.Notes.Add($"добавлен пункт {key}");
                    continue;
                }

                if (placeholderGuard && IsPlaceholder(t) && !IsPlaceholder(o)) continue;
                if (!IsNewer(t[CreatedAtProperty], o[CreatedAtProperty]))
                {
                    if (!ItemEquals(o, t, strict: false))
                        r.Notes.Add($"пункт {key}: в API новее — остаётся значение API");
                    continue;
                }

                bool changed = false;
                foreach (JProperty p in t.Properties())
                {
                    if (ValueEquals(o[p.Name], p.Value)) continue;
                    o[p.Name] = p.Value.DeepClone();
                    changed = true;
                }
                if (changed)
                {
                    r.Changed = true;
                    Taken(r, t, keyName, placeholderGuard);
                    r.Notes.Add($"пункт {key} из файла (новее)");
                }
            }
        }

        private static void Taken(Result r, JObject t, string keyName, bool autocheck)
        {
            if (!autocheck || IsPlaceholder(t)) return;
            if (t[keyName] is JValue v && v.Type == JTokenType.Integer) r.TakenNumbers.Add(v.Value<int>());
        }

        /// <summary>Заготовка автопроверки (проверку не запускали): без названия.</summary>
        private static bool IsPlaceholder(JObject item) => string.IsNullOrWhiteSpace(item.Value<string>("title"));

        private static bool IsNewer(JToken a, JToken b)
        {
            if (!TryDate(a, out DateTimeOffset da)) return false;
            if (!TryDate(b, out DateTimeOffset db)) return true;
            return da.UtcTicks > db.UtcTicks;
        }

        // ---------------- сравнение ----------------

        /// <summary>
        /// Документы совпадают по смыслу: те же ключи и пункты равны (порядок, photo_file_id,
        /// смещение часового пояса в датах не важны).
        /// </summary>
        public static bool Equivalent(string kind, JToken a, JToken b)
        {
            string keyName = kind == DocumentKinds.AutoCheck ? "Number" : "id";
            JArray aa = a as JArray ?? new JArray(), bb = b as JArray ?? new JArray();
            var ia = Index(aa, keyName);
            var ib = Index(bb, keyName);
            if (ia.Count != ib.Count) return false;
            foreach (var pair in ia)
            {
                if (!ib.TryGetValue(pair.Key, out JObject other) || !ItemEquals(pair.Value, other, strict: true))
                    return false;
            }
            // Пункты без ключа (не должно быть) — сравниваем по порядку.
            var restA = aa.Where(x => !(x is JObject o) || KeyOf(o, keyName) == null).ToList();
            var restB = bb.Where(x => !(x is JObject o) || KeyOf(o, keyName) == null).ToList();
            if (restA.Count != restB.Count) return false;
            for (int i = 0; i < restA.Count; i++)
                if (!JToken.DeepEquals(restA[i], restB[i])) return false;
            return true;
        }

        /// <summary>
        /// Пункты равны без учёта photo_file_id. strict = false: свойство, которого нет в одном
        /// из пунктов, не сравнивается (старая версия его не знает); strict = true — отсутствие = null.
        /// </summary>
        internal static bool ItemEquals(JObject a, JObject b, bool strict = true)
        {
            var names = new HashSet<string>(a.Properties().Select(p => p.Name).Concat(b.Properties().Select(p => p.Name)), StringComparer.Ordinal);
            names.Remove(PhotoFileIdProperty);
            foreach (string name in names)
            {
                JToken x = a[name], y = b[name];
                if (!strict && (x == null || y == null)) continue;
                if (!ValueEquals(x, y)) return false;
            }
            return true;
        }

        internal static bool ValueEquals(JToken a, JToken b)
        {
            bool an = a == null || a.Type == JTokenType.Null, bn = b == null || b.Type == JTokenType.Null;
            if (an || bn) return an && bn;
            if (a.Type == JTokenType.String && b.Type == JTokenType.String)
            {
                string sa = (string)a, sb = (string)b;
                if (sa == sb) return true;
                if (TryDate(a, out DateTimeOffset da) && TryDate(b, out DateTimeOffset db)) return da.UtcTicks == db.UtcTicks;
                return false;
            }
            return JToken.DeepEquals(a, b);
        }

        private static bool TryDate(JToken token, out DateTimeOffset value)
        {
            value = default;
            if (token == null || token.Type != JTokenType.String) return false;
            string s = (string)token;
            if (s == null || !IsoDate.IsMatch(s)) return false;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);
        }

        // ---------------- служебное ----------------

        private static Dictionary<string, JObject> Index(JArray arr, string keyName)
        {
            var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject o in arr.OfType<JObject>())
            {
                string k = KeyOf(o, keyName);
                if (k != null && !map.ContainsKey(k)) map[k] = o;
            }
            return map;
        }

        private static string KeyOf(JObject o, string keyName)
        {
            JToken v = o[keyName];
            if (v == null || v.Type == JTokenType.Null) return null;
            string s = v.Type == JTokenType.String ? (string)v : v.ToString();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static string Short(JToken t)
        {
            string s = t == null ? "null" : JsonConvert.SerializeObject(t, Formatting.None);
            return s.Length > 60 ? s.Substring(0, 57) + "..." : s;
        }

        // ---------------- формат ----------------

        /// <summary>Как у классов плагина: Formatting.Indented (JToken.ToString(Formatting) нет в Newtonsoft 11).</summary>
        public static string Serialize(JToken token) => JsonConvert.SerializeObject(token, Formatting.Indented);

        /// <summary>Копия без photo_file_id — так документ пишется в файл (старый CheckItem его не знает).</summary>
        public static JToken StripPhotoIds(JToken token)
        {
            JToken copy = token.DeepClone();
            foreach (JObject item in (copy as JArray ?? new JArray()).OfType<JObject>())
                item.Remove(PhotoFileIdProperty);
            return copy;
        }

        /// <summary>SHA-256 текста файла в UTF-8 без BOM (так пишет File.WriteAllText) — hex, строчные.</summary>
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte x in hash) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
