using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;

namespace TNovCommon
{
    public static class ServerUtils
    {
        
        public static bool CheckConnection(string TNovclassname,string TNovVersion)
        {
            UIDocument uidoc = RevitAPI.UiDocument;
            Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            TNovConfig config = TNovConfigLoad.LoadConfig();
            string usagefilePath = config.ServerPath+ "usage.txt"; //05.26 - уход от конфигураций
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " "); // --версия 1.0.2--
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, ""); // --версия 1.0.2--
            docName = docName.Replace(",", "");
            DateTime dateTime = DateTime.Now;
            string date = dateTime.ToString(); date = date.Replace(",", "");
            
            try
            {
                System.IO.File.AppendAllText(usagefilePath, "\n" + date + "," + userName + "," + docName + "," + TNovclassname + "," + TNovVersion);
                return true;
            }
            catch (Exception)
            {
                new InfoWindow280("Отсутствует подключение к корпоративной сети. Проверьте доступность сетевой папки или сетевого диска.").ShowDialog();
                return false;
            }
        }
    }
}
