using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TNovCommon.Help
{
    /// <summary>
    /// Кнопка ленты «Справка»: открывает панель справки.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ShowHelpPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (RevitAPI.UiApplication == null)
                RevitAPI.Initialize(commandData);

            if (!DockablePane.PaneIsRegistered(HelpPaneIds.Help))
            {
                message = "Панель справки не зарегистрирована. Перезапустите Revit.";
                return Result.Failed;
            }

            try
            {
                DockablePane pane = commandData.Application.GetDockablePane(HelpPaneIds.Help);
                pane.Show();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException e)
            {
                message = "Не удалось открыть панель справки: " + e.Message;
                return Result.Failed;
            }
        }
    }
}
