using System;
using System.Globalization;

namespace TNovCommon
{
    /// <summary>
    /// Минимальная версия плагина из tnovapi.json ("MinPluginVersion": "гг.ММдд[.минуты]").
    /// Версии всех модулей — «мажор.гг.ММдд.минуты» (UpdateVersion.ps1), поэтому сравниваем
    /// дату сборки TNovCommon без мажора: TNovCommon выпускается со всеми модулями вместе.
    /// </summary>
    public static class PluginVersionGate
    {
        /// <summary>Текст предупреждения, если эта сборка старее требуемой; иначе null.</summary>
        public static string OutdatedMessage(TNovConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.MinPluginVersion)) return null;
            if (!TryParse(config.MinPluginVersion, out long required)) return null;

            Version current = typeof(PluginVersionGate).Assembly.GetName().Version;
            if (current == null || Key(current.Minor, current.Build, Math.Max(current.Revision, 0)) >= required)
                return null;

            string hint = string.IsNullOrWhiteSpace(config.UpdateMessage)
                ? "Закройте Revit — TNovClient установит обновление при следующем запуске."
                : config.UpdateMessage.Trim();
            return $"Установлена устаревшая версия плагина TNov ({current}). " +
                   $"Требуется сборка не старее {config.MinPluginVersion.Trim()}.\n\n{hint}";
        }

        /// <summary>"26.0924" или "26.0924.749" (можно с мажором: "2.26.0924.749" — он игнорируется).</summary>
        internal static bool TryParse(string text, out long key)
        {
            key = 0;
            string[] parts = text.Trim().Split('.');
            if (parts.Length == 4) parts = new[] { parts[1], parts[2], parts[3] };
            if (parts.Length < 2 || parts.Length > 3) return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int yy)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int mmdd)) return false;
            int minutes = 0;
            if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)) return false;

            key = Key(yy, mmdd, minutes);
            return true;
        }

        private static long Key(int yy, int mmdd, int minutes) => (long)yy * 100_000_000 + (long)mmdd * 10_000 + minutes;
    }
}
