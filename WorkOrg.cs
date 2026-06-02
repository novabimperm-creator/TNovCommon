using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace TNovCommon
{
    [Transaction(TransactionMode.Manual)]
    public class WorkOrg : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //подключение приложения и документа
            DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            //Конфигурация
            string TNovClassName = "Методички"; TNovConfig config = TNovConfigLoad.LoadConfig(TNovClassName,TNovVersion); if(config==null) return Result.Failed;
            if (config.LicenseType != "corp")
            {
                new InfoWindow280("Данный функционал доступен только при наличии Корпоративной лицензии!").ShowDialog();
                return Result.Failed;
            }

            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();

            return Result.Succeeded;
        }
    }
}

