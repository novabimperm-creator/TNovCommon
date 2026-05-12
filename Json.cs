using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.IO;

namespace TNovCommon
{
    public class json
    {
        public json(in string TNovclassname, in bool forProject, out bool exist, out string jsonpath)
        {
            DateTime dateTime = DateTime.Now.Date;
            UIDocument uidoc = RevitAPI.UiDocument;
            Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application; 
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " "); 
            string userName = rvtApp.Username; string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, ""); 
            string date = dateTime.ToString(); date = date.Replace(":", "-"); date = date.Replace("/", "-"); date = date.Replace(" 0-00-00", "");
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            TNovConfig config = TNovConfigLoad.LoadConfig();
            jsonpath = $"{config.ServerPath}users/" + userName + "," + date + "," + TNovclassname + ".json";
            if (forProject) { jsonpath = config.ServerPath + "projects/" + docName + "," + TNovclassname + ".json"; }

            exist = false;
            try
            {
                exist = File.Exists(jsonpath);
            }
            catch (Exception) { }
        }
    }
}
