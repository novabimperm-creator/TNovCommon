using System;
using System.Globalization;

namespace TNovCommon
{
    /// <summary>Команда остановлена: плагин заблокирован сервером (см. <see cref="PluginVersionGate.BlockReason"/>).</summary>
    public sealed class PluginBlockedException : InvalidOperationException
    {
        public PluginBlockedException(string message) : base(message) { }
    }

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

        /// <summary>
        /// Команды, которые работают и при блокировке: по ним пользователь видит причину и версию.
        /// Имена — className, передаваемые в LoadConfig, и идентификаторы кнопок ленты.
        /// </summary>
        public static readonly string[] AllowedWhenBlocked =
        {
            "О программе",          // className окна версии/настроек (AppVersion)
            "AppVersion",           // кнопка «Настройки» на ленте
            "ShowHelpPaneCommand"   // кнопка «Справка»
        };

        /// <summary>
        /// Заблокирован ли плагин целиком (tnovapi.json):
        ///   "BlockBelowVersion" — эта сборка старее указанной;
        ///   "BlockFilesMode": true — сервер требует API, а локальный конфиг принудительно включил файлы.
        /// Нет политики или не прочитали её — не заблокирован. Возвращает текст причины или null.
        /// </summary>
        public static string BlockReason(TNovConfig config)
        {
            if (config == null) return null;
            string reason = null;

            if (!string.IsNullOrWhiteSpace(config.BlockBelowVersion) && TryParse(config.BlockBelowVersion, out long required))
            {
                Version current = typeof(PluginVersionGate).Assembly.GetName().Version;
                if (current != null && Key(current.Minor, current.Build, Math.Max(current.Revision, 0)) < required)
                    reason = $"Установленная версия плагина TNov ({current}) больше не поддерживается: " +
                             $"требуется сборка не старее {config.BlockBelowVersion.Trim()}.";
            }

            if (reason == null && config.BlockFilesMode
                && IsApi(config.ServerChecklistStorage) && !IsApi(config.ChecklistStorage))
                reason = "В локальной настройке TNovConfig.json чек-листы переключены на файлы на шаре, " +
                         "а сервер работает только через TNovApi. Уберите \"ChecklistStorage\" из " +
                         "%USERPROFILE%\\TNovClient\\TNovConfig.json.";

            if (reason == null) return null;
            string hint = string.IsNullOrWhiteSpace(config.BlockMessage)
                ? "Закройте Revit — TNovClient установит обновление при следующем запуске."
                : config.BlockMessage.Trim();
            return $"{reason}\n\nФункции плагина отключены.\n\n{hint}";
        }

        /// <summary>Разрешена ли команда при блокировке.</summary>
        public static bool IsAllowedWhenBlocked(string className) =>
            !string.IsNullOrEmpty(className) &&
            Array.Exists(AllowedWhenBlocked, n => string.Equals(n, className, StringComparison.OrdinalIgnoreCase));

        private static bool IsApi(string storage) =>
            string.Equals(storage?.Trim(), "api", StringComparison.OrdinalIgnoreCase);

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
