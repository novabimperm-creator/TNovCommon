using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;

namespace TNovCommon
{
    internal static class JournalCommandHelper
    {
        public static Result Prepare(
            ExternalCommandData commandData,
            string className,
            bool requireSavedDocument,
            out Document doc)
        {
            doc = null;
            if (RevitAPI.UiApplication == null)
                RevitAPI.Initialize(commandData);

            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            TNovConfig config = TNovConfigLoad.LoadConfig(className, version);
            if (config == null) return Result.Failed;

            if (config.LicenseType != "corp")
            {
                new InfoWindow280("Данный функционал доступен только при наличии Корпоративной лицензии!").ShowDialog();
                return Result.Failed;
            }

            UIDocument uidoc = RevitAPI.UiDocument;
            if (requireSavedDocument)
            {
                if (uidoc == null)
                {
                    new InfoWindow280("Нет открытой модели.").ShowDialog();
                    return Result.Failed;
                }

                doc = uidoc.Document;
                if (string.IsNullOrEmpty(doc.PathName) && !doc.IsWorkshared)
                {
                    new InfoWindow280("Сначала сохраните проект, чтобы начать совместную работу.").ShowDialog();
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SyncJournal : IExternalCommand
    {
        private static JournalWPF _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Result prepared = JournalCommandHelper.Prepare(
                commandData, "Журнал синхронизаций", requireSavedDocument: true, out Document doc);
            if (prepared != Result.Succeeded) return prepared;

            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            _window = JournalWPF.ShowOwned(
                "ЖУРНАЛ СИНХРОНИЗАЦИЙ",
                "Журнал проекта",
                new SyncJournalControl(doc));
            _window.Closed += (s, e) => _window = null;
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class TasksJournal : IExternalCommand
    {
        private static JournalWPF _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Result prepared = JournalCommandHelper.Prepare(
                commandData, "Журнал заданий", requireSavedDocument: false, out _);
            if (prepared != Result.Succeeded) return prepared;

            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var control = new TasksControl();
            control.RefreshData();
            _window = JournalWPF.ShowOwned(
                "ЖУРНАЛ ЗАДАНИЙ",
                "Задания",
                control);
            _window.Closed += (s, e) => _window = null;
            return Result.Succeeded;
        }
    }
}
