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
    public class TestSimpleCommand : IExternalCommand
    {
        


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            #region Исходные
            DateTime dateTime = DateTime.Now; 
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Команда 1";
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

                #region Настройки логов

                var viewModel0 = new AppVersionViewModel();

                try
                {
                    viewModel0 = Task.Run(() => dataService.LoadUserDataAsync<AppVersionViewModel>(user.UserId, "Настройки программы")).Result;
                }
                catch (Exception) { }

                if (viewModel0.extendedLogs)

                {
                    var qViewModel = new QuestionWindowViewModel();
                    qViewModel.headtxt = "Включены расширенные логи. " +
                        "Плагин будет работать медленнее, но соберет больше данных. " +
                        "Выключить расширенные логи для ускорения работы?";
                    var qwpfview = new QuestionWindow280(qViewModel);
                    qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                    bool? qok = qwpfview.ShowDialog();
                    if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл", 2);
                }
                #endregion


                #region Основной код

                Logger.Initialize(DBCommandName, dateTime, TNovVersion);
                Logger.Log("тестовая запись 1", 1);
                Logger.Log("тестовая запись 2", 2);

                #endregion

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
