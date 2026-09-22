using Autodesk.Revit.UI;

namespace TNovCommon.Help
{
    /// <summary>
    /// Поставщик содержимого панели справки.
    /// </summary>
    public class HelpPaneProvider : IDockablePaneProvider
    {
        /// <summary>
        /// Revit вызывает этот метод не один раз (dock/float, восстановление раскладки),
        /// поэтому он идемпотентен: контрол создаётся один раз и всегда возвращается тот же.
        /// FrameworkElement, а не FrameworkElementCreator — второй предназначен для элементов,
        /// которые нельзя кэшировать между вызовами.
        /// </summary>
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = HelpPaneHost.GetOrCreateControl();

            // InitialState применяется только в первой сессии, где панель зарегистрирована;
            // дальше Revit помнит положение, выбранное пользователем.
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                // Без минимума панель утаскивается в нулевую ширину, и текст рендерится мусором.
                MinimumWidth = 340,
                MinimumHeight = 200
            };

            // По умолчанию true — панель сама открылась бы у всех после обновления плагина.
            data.VisibleByDefault = false;

            // Явно: Dismiss сбрасывает активное выделение, справка не имеет на это права.
            data.EditorInteraction = new EditorInteraction(EditorInteractionType.KeepAlive);

            data.ContextualHelp = new ContextualHelp(ContextualHelpType.Url, HelpLinks.GetHelpLink("-"));
        }
    }
}
