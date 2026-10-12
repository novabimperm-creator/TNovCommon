using System;
using System.Collections.Generic;

namespace TNovCommon.Server
{
    /// <summary>
    /// Временная подмена адреса Revit Server без правки RSpath.txt и RS.txt — например, на время
    /// переезда сервера с rvt-nova на rvt-nova1. Правила лежат в {ServerPath}RSredirect.txt:
    /// <code>
    /// # старый => новый
    /// rvt-nova => rvt-nova1
    /// </code>
    /// Разделитель «=&gt;», «-&gt;», «=» или табуляция; «#» и «::» — комментарии. Нет файла или он пустой —
    /// пути не меняются. Сравнение без учёта регистра; правило для «rvt-nova» действует и на
    /// «rvt-nova.domain.local» (заменяется префикс до точки/двоеточия), но не на «rvt-nova2».
    /// Файл читается через <see cref="ServerData"/>, поэтому правка вступает в силу в течение 5 минут.
    /// </summary>
    public static class RevitServerRedirect
    {
        public const string FileName = "RSredirect.txt";

        private const string RsnPrefix = "RSN:";

        /// <summary>
        /// Путь с подменённым сервером: принимает полный путь «RSN://сервер/…» («RSN:\\сервер\…»)
        /// или одно имя сервера (содержимое RSpath.txt). Остальное возвращает как есть.
        /// </summary>
        public static string Apply(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            List<KeyValuePair<string, string>> rules = LoadRules();
            if (rules.Count == 0) return path;

            // Разбор: [RSN: + слеши][сервер][остаток пути]
            int hostStart = 0;
            if (path.StartsWith(RsnPrefix, StringComparison.OrdinalIgnoreCase))
            {
                hostStart = RsnPrefix.Length;
                while (hostStart < path.Length && (path[hostStart] == '/' || path[hostStart] == '\\')) hostStart++;
            }
            int hostEnd = path.IndexOfAny(new[] { '/', '\\' }, hostStart);
            if (hostEnd < 0) hostEnd = path.Length;
            string host = path.Substring(hostStart, hostEnd - hostStart).Trim();

            foreach (var rule in rules)
            {
                string newHost = ReplaceHost(host, rule.Key, rule.Value);
                if (newHost != null)
                    return path.Substring(0, hostStart) + newHost + path.Substring(hostEnd);
            }
            return path;
        }

        /// <summary>Была ли подмена (для лога команд).</summary>
        public static bool TryApply(string path, out string redirected)
        {
            redirected = Apply(path);
            return !string.Equals(redirected, path, StringComparison.Ordinal);
        }

        private static string ReplaceHost(string host, string oldHost, string newHost)
        {
            if (!host.StartsWith(oldHost, StringComparison.OrdinalIgnoreCase)) return null;
            if (host.Length == oldHost.Length) return newHost;
            char next = host[oldHost.Length];
            return next == '.' || next == ':' ? newHost + host.Substring(oldHost.Length) : null;
        }

        private static List<KeyValuePair<string, string>> LoadRules()
        {
            var rules = new List<KeyValuePair<string, string>>();
            // Файла нет (обычная ситуация) или шара недоступна — работаем без подмены.
            if (!ServerData.TryReadAllLines(FileName, out string[] lines)) return rules;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("::")) continue;

                string[] parts = line.Split(new[] { "=>", "->", "=", "\t" }, 2, StringSplitOptions.None);
                if (parts.Length != 2) continue;
                string from = CleanHost(parts[0]);
                string to = CleanHost(parts[1]);
                if (from.Length == 0 || to.Length == 0) continue;
                rules.Add(new KeyValuePair<string, string>(from, to));
            }
            return rules;
        }

        // Допускаем в правилах «RSN://rvt-nova/» и кавычки — оставляем только имя сервера.
        private static string CleanHost(string value)
        {
            string s = value.Trim().Trim('"').Trim();
            if (s.StartsWith(RsnPrefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(RsnPrefix.Length);
            return s.Trim('/', '\\', ' ');
        }
    }
}
