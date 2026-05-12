using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TNovCommon
{
    [Transaction(TransactionMode.Manual)]
    public class PluginSettingsCommand : IExternalCommand
    {
        


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            #region Исходные
            DateTime dateTime = DateTime.Now; 
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Настройки программы";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion
            
            try
            {

                // ---------- ТОЛЬКО ДЛЯ ЛОКАЛЬНОГО ТЕСТИРОВАНИЯ: создать таблицы, если их нет ----------
                /* Раскомментируйте для первого запуска, затем уберите
                    // ---------- ТОЛЬКО ДЛЯ ДИАГНОСТИКИ ----------
                    try
                    {
                        Task.Run(() => DatabaseInitializer.InitializeAsync()).Wait();
                    }
                    catch (AggregateException ae)
                    {
                        // Показываем все внутренние исключения
                        string errMsg = "";
                        foreach (var inner in ae.InnerExceptions)
                            errMsg += inner.Message + "\n" + inner.StackTrace + "\n\n";
                        TaskDialog.Show("Ошибка инициализации БД", errMsg);
                        return Result.Failed;
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Ошибка", ex.ToString());
                        return Result.Failed;
                    }
                    // ---------------------------------------------
                */
                #region БД
                //аутентификация
                IAuthProvider authProvider = TNovProvider.GetAuthProvider();

                UserInfo user = AuthenticationService.Authenticate(authProvider);
                if (user == null)
                    return Result.Cancelled;

                var repo = new PostgresRepository(ConnectionStringProvider.GetConnectionString());

                var userTask = Task.Run(() => repo.GetOrCreateUserAsync(user.Upn, user.DisplayName));
                if (!userTask.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Не удалось подключиться к БД (превышен таймаут).");
                user = userTask.Result; //получаем user из БД

                FunctionLogger.Log(repo, DBCommandName, user.Upn); //запись в usage
                
                //работа с данными
                var dataService = new DataService(repo);
                #endregion

                #region Основной код

                string userDepartment = "";
                string userDepRole = "";
                if(user.Department!=null) userDepartment = user.Department;
                if (user.RuleRole) userDepRole = "Руководитель"; else userDepRole = "Исполнитель";

                var viewModel = new AppVersionViewModel();

                try
                {
                    viewModel = Task.Run(() => dataService.LoadUserDataAsync<AppVersionViewModel>(user.UserId, DBCommandName)).Result;
                }
                catch (Exception e) { new InfoWindow280($"Ошибка при загрузке данных из базы: {e.Message}").ShowDialog(); }

                viewModel.headtxt = $"Настройки";
                viewModel.url = "https://portal.talan.group/knowledge/proektirovanie/plaginyiskriptynovatsiya/";
                viewModel.userName = userName; viewModel.userDep = userDepartment; viewModel.userDepRole = userDepRole;
                var wpfview = new AppVersionWPF(viewModel);
                viewModel.CloseRequest += (s, e) => wpfview.Close();
                bool? ok = wpfview.ShowDialog();
                if (ok != null && ok == true) { } else { return Result.Cancelled; }

                try
                {
                    Task.Run(() =>
                        dataService.SaveUserDataAsync(user.UserId, DBCommandName, viewModel)).Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception e) { new InfoWindow280($"Ошибка при сохранении данных в базу: {e.Message}").ShowDialog(); }


                #endregion

                /*
                var editor = new TextEditorWindow(dataService, user.UserId, DBCommandName);
                editor.ShowDialog();

                var editor1 = new TextEditorWindow(dataService, docName, DBCommandName);
                editor1.ShowDialog();*/

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Критическая ошибка: {ex.Message}";
                return Result.Failed;
            }

        }
        
    }
}
