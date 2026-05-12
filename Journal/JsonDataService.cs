using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class JsonDataService
    {
        private static readonly object _fileLock = new object();

        // Получаем путь к JSON файлу в конкретной сетевой папке
        public static string GetJsonPath(Document doc, string name)
        {
            if (doc == null) return null;

            string rvtPath = string.Empty;

            try
            {
                rvtPath = doc.PathName;
            }
            catch { return null; }

            if (string.IsNullOrEmpty(rvtPath)) return null;

            TNovConfig config = TNovConfigLoad.LoadConfig();

            string directory = Path.Combine($"{config.ServerPath}projects");

            

            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application; 
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");

            
            return Path.Combine(directory, $"{docName},{name}.json");

        }
        

        public static void Save(string jsonPath, List<CheckItem> items)
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            string json = JsonConvert.SerializeObject(items, settings);

            lock (_fileLock)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        File.WriteAllText(jsonPath, json);
                        return;
                    }
                    catch (IOException) { Thread.Sleep(300); }
                }
                throw new IOException($"Не удалось сохранить файл {jsonPath} после трёх попыток.");
            }
        }

        public static void SaveAuto(string jsonPath, List<AutoCheckItem> items)
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            string json = JsonConvert.SerializeObject(items, settings);

            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            Document doc = RevitAPI.Document;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");

            #region БД
            //аутентификация
            IAuthProvider authProvider = TNovProvider.GetAuthProvider();

            UserInfo user = AuthenticationService.Authenticate(authProvider);
            if (user == null)
                return;

            var repo = new PostgresRepository(ConnectionStringProvider.GetConnectionString());

            var userTask = Task.Run(() => repo.GetOrCreateUserAsync(user.Upn, user.DisplayName));
            if (!userTask.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Не удалось подключиться к БД (превышен таймаут).");
            user = userTask.Result; //получаем user из БД

            //работа с данными
            var dataService = new DataService(repo);
            
            AsyncHelper.RunSync(() => dataService.SaveModelDataAsync(docName, "Автопроверки", json));
            #endregion

            lock (_fileLock)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        File.WriteAllText(jsonPath, json);
                        return;
                    }
                    catch (IOException) { Thread.Sleep(300); }
                }
                throw new IOException($"Не удалось сохранить файл {jsonPath} после трёх попыток.");
            }

            
        }

        public static List<CheckItem> Load(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
                return new List<CheckItem>();

            lock (_fileLock)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        return JsonConvert.DeserializeObject<List<CheckItem>>(json) ?? new List<CheckItem>();
                    }
                    catch (IOException) { Thread.Sleep(300); }
                }
                throw new IOException($"Не удалось прочитать файл {jsonPath} после трёх попыток.");
            }
        }

   

    public static List<AutoCheckItem> LoadAuto(string jsonPath, DateTime dateTime)
        {
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string userName = rvtApp.Username;

            //определяем базовый список автопроверок
            BaseItems baseItems = new BaseItems();
            List<AutoCheckItem> baseAutoCheckItems = baseItems.GetBaseItems(dateTime);

            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) //файл отсутствует -
                return baseAutoCheckItems; //возвращаем базовый список

            lock (_fileLock)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        //возвращаем список ранее пройденных проверок + базовые, если они ранее не были пройдены
                        List<AutoCheckItem> currentAutoCheckItems = JsonConvert.DeserializeObject<List<AutoCheckItem>>(json) ?? baseAutoCheckItems;
                        List<AutoCheckItem> checkItemsToWork = new List<AutoCheckItem>();
                        foreach (var baseItem in baseAutoCheckItems)
                        {
                            bool itemExist = false;
                            foreach (var currentItem in currentAutoCheckItems)
                            {
                                if(currentItem.Number == baseItem.Number)
                                {
                                    itemExist = true;
                                    checkItemsToWork.Add(currentItem);
                                    break;
                                }
                            }
                            if(!itemExist) checkItemsToWork.Add(baseItem);
                        }
                        return checkItemsToWork;
                    }
                    catch (IOException) { Thread.Sleep(300); }
                }
                throw new IOException($"Не удалось прочитать файл {jsonPath} после трёх попыток.");
            }
        }
    }
 }