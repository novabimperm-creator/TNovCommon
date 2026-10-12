using System;

namespace TNovCommon
{
    /// <summary>
    /// Корпоративная конфигурация (LicenseType = "corp") — есть сервер офиса, TNovPRO, Revit Server.
    /// Любое другое значение — личная конфигурация: «сервер» — локальная папка TNovClient.
    /// </summary>
    public static class CorpGate
    {
        public const string Message = "Данный функционал доступен только в корпоративной конфигурации!";

        public static bool IsCorp(TNovConfig config) =>
            config != null && string.Equals(config.LicenseType, "corp", StringComparison.OrdinalIgnoreCase);

        /// <summary>Текущая конфигурация корпоративная (без диалогов; false, если конфиг не читается).</summary>
        public static bool IsCorp() => IsCorp(TNovConfigLoad.GetCachedConfig());

        /// <summary>
        /// Проверка в начале корпоративной команды: в личной конфигурации показывает сообщение
        /// и возвращает false — команду нужно завершить.
        /// </summary>
        public static bool Require(TNovConfig config)
        {
            if (IsCorp(config)) return true;
            new InfoWindow280(Message).ShowDialog();
            return false;
        }
    }
}
