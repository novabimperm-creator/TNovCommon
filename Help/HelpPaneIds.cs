using System;
using Autodesk.Revit.UI;

namespace TNovCommon.Help
{
    /// <summary>
    /// Идентификатор панели справки. GUID зафиксирован навсегда: по нему Revit
    /// запоминает положение панели в раскладке пользователя.
    /// </summary>
    public static class HelpPaneIds
    {
        /// <summary>Заголовок панели в интерфейсе Revit.</summary>
        public const string Title = "TNov · Справка";

        private static readonly Guid HelpGuid = new Guid("25CCE872-19C5-465E-AC6E-42D9EF0656E6");

        /// <summary>
        /// DockablePaneId не IDisposable — держать статически безопасно
        /// (в отличие от DockablePane, который возвращает GetDockablePane).
        /// </summary>
        public static readonly DockablePaneId Help = new DockablePaneId(HelpGuid);
    }
}
