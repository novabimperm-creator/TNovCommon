using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TNovCommon
{
    /// <summary>
    /// Логика взаимодействия для SyncJournalControl.xaml
    /// </summary>
    public partial class SyncJournalControl : UserControl
    {
        public SyncJournalControl(Document doc)
        {
            InitializeComponent();

            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;

            
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            TNovConfig config = TNovConfigLoad.LoadConfig();
            string usagefilePath = $"{config.ServerPath}projects/{docName},synchronizes.txt";
            if (!File.Exists(usagefilePath)) 
            {
                text1.Text = "В журнале пока отсутствуют записи о синхронизациях модели " + docName + ".\nСтатистика ведется, скоро данные появятся!";

            }
            else
            {
                string[] lines = File.ReadAllLines(usagefilePath);


                List<string> docLines = new List<string>();

                foreach (var line in lines)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length > 2 && parts[2].Equals(docName)) docLines.Add(parts[0] + "   " + parts[1]);
                }

                if (docLines.Count > 0)
                {
                    int i = 0;
                    string mes = "";
                    docLines.Reverse();
                    foreach (var line in docLines)
                    {
                        i++;
                        if (i > 1000) break;
                        mes += "\n" + line;
                    }
                    text1.Text = mes;
                }
                else text1.Text = "В журнале пока отсутствуют записи о синхронизациях модели " + docName + ".\nСтатистика ведется с 03.04.2025, скоро данные появятся!";

            }



        }
        
    }

}
