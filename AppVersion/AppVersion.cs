using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using adWin = Autodesk.Windows;
using File = System.IO.File;

namespace TNovCommon
{
    
    
    [Transaction(TransactionMode.Manual)]
    public class AppVersion : IExternalCommand
    {
        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string TNovClassName = "О программе"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            //Конфигурация
            TNovConfig config = TNovConfigLoad.LoadConfig(TNovClassName,TNovVersion); if(config==null) return Result.Failed;

            string companyName = config.CorpName; //компания
            //имя и роль пользователя
            string userName = rvtApp.Username;
            string userDepartment = "";
            string userDepRole = "";

            if (config.LicenseType=="corp")
            {
                string[] rolesFile = File.ReadAllLines($"{config.ServerPath}roles.txt");
                foreach (string role in rolesFile)
                {
                    if (role.Contains(userName))
                    {
                        string[] line = role.Split(','); userDepartment = line[1]; userDepRole = line[2]; break;
                    }

                }
                switch (userDepartment)
                {
                    case "AR": userDepartment = "АР"; break;
                    case "ST": userDepartment = "КР"; break;
                    case "VK": userDepartment = "ВК"; break;
                    case "OV": userDepartment = "ОВ"; break;
                    case "EL": userDepartment = "ЭО"; break;
                    case "SS": userDepartment = "СС"; break;
                }
                switch (userDepRole)
                {
                    case "head": userDepRole = "руководитель"; break;
                    case "user": userDepRole = "исполнитель"; break;
                }
            }

            var viewModel = new AppVersionViewModel();
            // Десериализация
            string jsonpath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            try
            {
                viewModel = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath));
            }
            catch (Exception) { }

            string licenseType = ""; if (config.LicenseType == "corp") licenseType = "Корпоративная"; else licenseType = "Личная";
            if (companyName.Length > 0) companyName = $"Компания: {companyName}";

            viewModel.headtxt = $"Лицензия: {licenseType} {companyName}";
            viewModel.url = "https://portal.talan.group/knowledge/proektirovanie/plaginyiskriptynovatsiya/";
            viewModel.userName = userName; viewModel.userDep = userDepartment; viewModel.userDepRole = userDepRole;
            var wpfview = new AppVersionWPF(viewModel);
            viewModel.CloseRequest += (s, e) => wpfview.Close();
            bool? ok = wpfview.ShowDialog();
            if (ok != null && ok == true) { } else { return Result.Cancelled; }
            //Сериализация
            try
            {
                File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
            }
            catch (Exception) { }

            if(viewModel.sync1== "Без подсветки панелей (не рекомендуется)")
            {
                adWin.RibbonControl ribbon = adWin.ComponentManager.Ribbon;
                foreach (adWin.RibbonTab tab in ribbon.Tabs)
                {
                    foreach (adWin.RibbonPanel panel in tab.Panels)
                    {
                        panel.CustomPanelBackground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F6F6F6");
                        panel.CustomPanelTitleBarBackground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F6F6F6");

                    }
                }
            }

            return Result.Succeeded;
        }
    }
}
