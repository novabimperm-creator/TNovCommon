using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.DirectoryServices.AccountManagement;

namespace TNovCommon
{
    public class UserInfo
    {
        public Guid UserId { get; set; }
        public string Upn { get; set; }
        public string DisplayName { get; set; }
        public string Department { get; set; }
        public bool RuleRole { get; set; }
    }

    public class FunctionDataEntry
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string FunctionName { get; set; }
        public string DataJson { get; set; }  // сырой JSON
        public DateTime UpdatedAt { get; set; }
    }
    public class ModelDataEntry
    {
        public Guid Id { get; set; }
        public string ModelName { get; set; }
        public string FunctionName { get; set; }
        public string DataJson { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    public interface IDataRepository
    {
        Task<FunctionDataEntry> LoadAsync(Guid userId, string functionName);
        Task SaveAsync(Guid userId, string functionName, string jsonData);
        Task<UserInfo> GetOrCreateUserAsync(string upn, string displayName);
        Task<ModelDataEntry> LoadForModelAsync(string modelName, string functionName);
        Task SaveForModelAsync(string modelName, string functionName, string jsonData);
        Task LogFunctionUsageAsync(string functionName, string userName, string version);
    }
    public interface IAuthProvider
    {
        // Прежний метод – проверка по паре upn + пароль (для ручного ввода)
        Task<UserInfo> AuthenticateAsync(string upn, string password);

        // Новый метод – получить текущего доменного пользователя Windows без пароля
        Task<UserInfo> AuthenticateCurrentUserAsync();
    }

    public static class ConnectionStringProvider
    {
        // ===== ЛОКАЛЬНОЕ ПОДКЛЮЧЕНИЕ (раскомментировано) =====
        private static readonly string _connectionString =
            "Host=localhost;Port=5432;Database=TNov;Username=postgres;Password=Oanwts89!;";

        // ===== СЕРВЕРНОЕ ПОДКЛЮЧЕНИЕ (закомментировано для будущего использования) =====
        // private static readonly string _connectionString =
        //    "Host=192.168.0.100;Port=5432;Database=TNov;Username=plugin_user;Password=strong_password;SSL Mode=Prefer;";

        public static string GetConnectionString() => _connectionString;
    }
    
    public static class TNovProvider
    {
        public static IAuthProvider GetAuthProvider()
        {


            return new SimpleAuthProvider();
            //return new ActiveDirectoryAuthProvider("company.local", "dc01.company.local");
        }
    }

    public class TNovConfig
    {
        public string LicenseType { get; set; }
        public string CorpName { get; set; }
        public string ServerPath { get; set; }
    }
    public static class TNovConfigLoad
    {
        public static TNovConfig LoadConfig() 
        {
            string clientFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient");
            string configPath = Path.Combine(clientFolderPath, "TNovConfig.json");

            try
            {
                string jsonContent = File.ReadAllText(configPath);
                TNovConfig config = JsonConvert.DeserializeObject<TNovConfig>(jsonContent);
                return config;
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при чтении файла конфигурации: {ex.Message}").ShowDialog();
                return null;
            }
        }
        public static TNovConfig LoadConfig(string className, string version)
        {
            string clientFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient");
            string configPath = Path.Combine(clientFolderPath, "TNovConfig.json");

            try
            {
                string jsonContent = File.ReadAllText(configPath);
                TNovConfig config = JsonConvert.DeserializeObject<TNovConfig>(jsonContent);
                 
                //запись в файл usage (при любом типе лицензии)
                
                UIDocument uidoc = RevitAPI.UiDocument;
                Document doc = RevitAPI.Document;
                UIApplication uiApp = RevitAPI.UiApplication;
                Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
                string usagefilePath = config.ServerPath + "usage.txt"; //в перспективе - заменить таблицу usage на сайтовскую
                string docName = doc.Title.ToString(); docName = docName.Replace(",", " "); 
                string userName = rvtApp.Username; userName = userName.Replace(",", "");
                string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, ""); 
                docName = docName.Replace(",", "");
                DateTime dateTime = DateTime.Now;
                string date = dateTime.ToString(); date = date.Replace(",", "");

                try
                {
                    System.IO.File.AppendAllText(usagefilePath, "\n" + date + "," + userName + "," + docName + "," + className + "," + version);
                }
                catch (Exception e) { new InfoWindow280($"Ошибка добавлении записи о запуске: {e.Message}").ShowDialog(); }

                return config;
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при чтении файла конфигурации: {ex.Message}").ShowDialog();
                return null;
            }
        }
    }
}
