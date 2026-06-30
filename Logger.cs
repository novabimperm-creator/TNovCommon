using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.VisualBasic.ApplicationServices;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace TNovCommon
{
    
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static bool _extendedLogs;

        /// <summary>
        /// Инициализация логгера с указанием серверной папки
        /// </summary>
        public static void Initialize(string TNovclassname,DateTime dateTime,string TNovVersion)
        {
            UIDocument uidoc = RevitAPI.UiDocument;
            Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            string date = dateTime.ToString(); date = date.Replace(":", "-");

            string clientFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient");
            string logPath = $"{clientFolderPath}/logs/log,{date},{userName},{docName},{TNovclassname},{TNovVersion}.txt";

            var viewModel0 = new AppVersionViewModel();
            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            try { viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0)); }
            catch(Exception) 
            {
                //Сериализация
                try
                {
                    File.WriteAllText(jsonpath0, JsonConvert.SerializeObject(viewModel0));
                }
                catch (Exception) { }
            }/*
            //десериализация из БД (приоритетнее)
            IAuthProvider authProvider = TNovProvider.GetAuthProvider();
            UserInfo user = AuthenticationService.Authenticate(authProvider);
            if (user != null)
            {
                var repo = new PostgresRepository(ConnectionStringProvider.GetConnectionString());

                var userTask = Task.Run(() => repo.GetOrCreateUserAsync(user.Upn, user.DisplayName));
                if (!userTask.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Не удалось подключиться к БД (превышен таймаут).");
                user = userTask.Result; //получаем user из БД
                var dataService = new DataService(repo);
                try
                {
                    viewModel0 = Task.Run(() => dataService.LoadUserDataAsync<AppVersionViewModel>(user.UserId, "Настройки программы")).Result;
                }
                catch (Exception) { }
            }
            */

            try
            {
                // Генерация уникального имени файла с временной меткой
                _logFilePath = logPath;

                // Расширенные логи
                _extendedLogs = viewModel0.extendedLogs;

                // Запись стартового сообщения
                Log("Логгер инициализирован",0);
            }
            catch (Exception)
            {
                // Обработка ошибок инициализации
                // (можно добавить дополнительную логику)
            }
        }

        /// <summary>
        /// Отключение
        /// </summary>
        public static void TurnOffExtendedLogs()
        {
            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            var viewModel0 = new AppVersionViewModel();
            
            try
            {
                viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0));
                viewModel0.extendedLogs = false;
                // Откл расширенные логи в настройках TNov
                File.WriteAllText(jsonpath0, JsonConvert.SerializeObject(viewModel0));
                /*
                // Откл расширенные логи в базе
                IAuthProvider authProvider = TNovProvider.GetAuthProvider();
                UserInfo user = AuthenticationService.Authenticate(authProvider);
                if (user != null)
                {
                    var repo = new PostgresRepository(ConnectionStringProvider.GetConnectionString());

                    var userTask = Task.Run(() => repo.GetOrCreateUserAsync(user.Upn, user.DisplayName));
                    if (!userTask.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Не удалось подключиться к БД (превышен таймаут).");
                    user = userTask.Result; //получаем user из БД
                    var dataService = new DataService(repo);
                    Task.Run(() =>
                        dataService.SaveUserDataAsync(user.UserId, "Настройки программы", viewModel0)).Wait(TimeSpan.FromSeconds(5));
                }
                */
                // Откл расширенные логи при текущем запуске
                _extendedLogs = false;

            }
            catch (Exception)
            {
                // Обработка ошибок инициализации
                // (можно добавить дополнительную логику)
            }
        }


        /// <summary>
        /// Запись сообщения в лог:
        /// 0 - старт работы (START), 1 - этап выполнения (INFO), 2 - технический этап (TECH), 
        /// 3 - преждевременный конец (BREAK), 4 - ошибка (ERROR), 5 - окончание работы (END).
        /// Все расширенные логи относить к 2 (TECH).
        /// </summary>
        public static void Log(string message, int level)
        {
            string levelStr = "TECH";
            switch (level)
            {
                case 0:
                    levelStr = "START"; break;
                case 1:
                    levelStr = "INFO"; break;
                case 2:
                    levelStr = "TECH"; break;
                case 3:
                    levelStr = "BREAK"; break;
                case 4:
                    levelStr = "ERROR"; break;
                case 5:
                    levelStr = "END"; break;

            }
            lock (_lock)
            {
                try
                {
                    string logEntry = $"{DateTime.Now:HH:mm:ss} [{levelStr}] {message}";
                    if(level==2&&_extendedLogs==false) { }
                    else File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Обработка ошибок записи
                }
            }
        }
        public static void Log(string message)
        {
            string levelStr = "INFO";
            lock (_lock)
            {
                try
                {
                    string logEntry = $"{DateTime.Now:HH:mm:ss} [{levelStr}] {message}";
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Обработка ошибок записи
                }
            }
        }
    }
}
