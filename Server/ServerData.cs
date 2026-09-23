using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace TNovCommon.Server
{
    /// <summary>
    /// Кэш справочников с сервера (roles.txt, CDE.txt, RS.txt, RSpath.txt …).
    /// Каждое чтение по SMB — несколько round-trip'ов, а из удалённого офиса это
    /// сотни миллисекунд. Справочники меняются редко, поэтому держим их в памяти
    /// процесса Revit и перечитываем не чаще, чем раз в <see cref="DefaultTtl"/>.
    /// Если сервер недоступен — отдаём последнюю удачно прочитанную версию.
    /// </summary>
    public static class ServerData
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        private sealed class Entry
        {
            public string Text;
            public string[] Lines;
            public DateTime LoadedUtc;
        }

        private static readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Полный путь к файлу в корне серверной папки текущей конфигурации.</summary>
        public static string PathOf(string relativePath)
        {
            TNovConfig config = TNovConfigLoad.GetCachedConfig();
            if (config == null || string.IsNullOrEmpty(config.ServerPath))
                throw new InvalidOperationException("Не задана серверная папка (TNovConfig.json).");
            return Combine(config.ServerPath, relativePath);
        }

        internal static string Combine(string serverPath, string relativePath)
        {
            // ServerPath исторически хранится со слешем на конце и склеивается строкой.
            string root = serverPath.EndsWith("\\") || serverPath.EndsWith("/") ? serverPath : serverPath + "\\";
            return root + relativePath.TrimStart('\\', '/');
        }

        /// <summary>Строки файла из корня серверной папки (с кэшем).</summary>
        public static string[] ReadAllLines(string relativePath) => ReadAllLines(relativePath, DefaultTtl);

        // Копия массива: вызывающий код может его менять, а кэш общий.
        public static string[] ReadAllLines(string relativePath, TimeSpan ttl) =>
            (string[])Get(PathOf(relativePath), ttl).Lines.Clone();

        /// <summary>Текст файла из корня серверной папки (с кэшем).</summary>
        public static string ReadAllText(string relativePath) => ReadAllText(relativePath, DefaultTtl);

        public static string ReadAllText(string relativePath, TimeSpan ttl) =>
            Get(PathOf(relativePath), ttl).Text;

        /// <summary>Строки файла по полному пути (с кэшем) — для путей вне корня конфигурации.</summary>
        public static string[] ReadAllLinesAbsolute(string fullPath) => (string[])Get(fullPath, DefaultTtl).Lines.Clone();

        /// <summary>Мягкий вариант: false, если файла нет и в кэше тоже ничего нет.</summary>
        public static bool TryReadAllLines(string relativePath, out string[] lines)
        {
            try
            {
                lines = ReadAllLines(relativePath);
                return true;
            }
            catch (Exception)
            {
                lines = null;
                return false;
            }
        }

        public static bool TryReadAllText(string relativePath, out string text)
        {
            try
            {
                text = ReadAllText(relativePath);
                return true;
            }
            catch (Exception)
            {
                text = null;
                return false;
            }
        }

        /// <summary>Сбросить кэш (например, после сохранения справочника из плагина).</summary>
        public static void Invalidate(string relativePath = null)
        {
            if (relativePath == null) { _cache.Clear(); return; }
            try { _cache.TryRemove(PathOf(relativePath), out _); } catch (Exception) { }
        }

        private static Entry Get(string fullPath, TimeSpan ttl)
        {
            DateTime now = DateTime.UtcNow;
            if (_cache.TryGetValue(fullPath, out Entry cached) && now - cached.LoadedUtc < ttl)
                return cached;

            try
            {
                string text = File.ReadAllText(fullPath, Encoding.UTF8);
                var entry = new Entry
                {
                    Text = text,
                    Lines = SplitLines(text),
                    LoadedUtc = now
                };
                _cache[fullPath] = entry;
                return entry;
            }
            catch (Exception)
            {
                // Сеть моргнула — лучше устаревший справочник, чем отказ команды.
                if (cached != null)
                {
                    cached.LoadedUtc = now - ttl + TimeSpan.FromSeconds(30); // повторим через 30 с
                    return cached;
                }
                throw;
            }
        }

        // Совпадает с поведением File.ReadAllLines: \r\n, \n и \r; последний пустой хвост отбрасывается.
        private static string[] SplitLines(string text)
        {
            if (text.Length == 0) return new string[0];
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                Array.Resize(ref lines, lines.Length - 1);
            return lines;
        }
    }
}
