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
            string usagefilePath = nova.novaserver + "_TNov/usage.txt";
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
                string info1txt = "Отсутствует подключение к корпоративной сети ПМ Новация. Проверьте подключение к адресу fs-nova.";
                new InfoWindow280(info1txt).ShowDialog();
                return false;
            }
        }
    }
}
