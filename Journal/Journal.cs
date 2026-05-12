using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using File = System.IO.File;

namespace TNovCommon
{
    [Transaction(TransactionMode.Manual)]
    public class Journal : IExternalCommand
    {
        private static JournalWPF _window;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string TNovClassName = "Журнал синхронизаций"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            //Конфигурация
            TNovConfig config = TNovConfigLoad.LoadConfig(TNovClassName,TNovVersion); if(config==null) return Result.Failed;

            if (config.LicenseType != "corp")
            {
                new InfoWindow280("Данный функционал доступен только при наличии Корпоративной лицензии!").ShowDialog();
                return Result.Failed;
            }

            if (string.IsNullOrEmpty(doc.PathName) && !doc.IsWorkshared)
            {
                new InfoWindow280("Сначала сохраните проект, чтобы начать совместную работу.").ShowDialog();
                return Result.Failed;
            }

            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            // Передаем документ в окно
            _window = new JournalWPF(uidoc);

            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                _window.SetOwner(handle);
            }
            catch { }

            _window.Show();

            
            return Result.Succeeded;
        }
    }
}
