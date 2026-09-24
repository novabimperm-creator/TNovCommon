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

        /// <summary>Адрес TNovApi, например http://tnov-api:5090/ (пусто — API не используется).</summary>
        public string ApiUrl { get; set; }
        /// <summary>Ключ доступа к TNovApi (заголовок X-TNov-Key). Временная схема до входа по учётке Windows.</summary>
        public string ApiKey { get; set; }
        /// <summary>Где хранятся чек-листы: "files" (по умолчанию, шара) или "api".</summary>
        public string ChecklistStorage { get; set; }
        /// <summary>
        /// Режим API на переходный период: подтягивать правки старых версий из файлов шары и
        /// дублировать сохранения в файлы (см. Storage\FileSync). null/true — включено, false — выключено.
        /// </summary>
        public bool? FileSync { get; set; }

        /// <summary>Минимальная версия плагина «гг.ММдд[.минуты]» (только из tnovapi.json).</summary>
        [JsonIgnore] public string MinPluginVersion { get; set; }
        /// <summary>Текст предупреждения для устаревших версий (только из tnovapi.json).</summary>
        [JsonIgnore] public string UpdateMessage { get; set; }

        /// <summary>Жёсткая блокировка сборок старше «гг.ММдд[.минуты]» (только из tnovapi.json).</summary>
        [JsonIgnore] public string BlockBelowVersion { get; set; }
        /// <summary>Блокировать, если сервер требует API, а локально включены файлы (только из tnovapi.json).</summary>
        [JsonIgnore] public bool BlockFilesMode { get; set; }
        /// <summary>Текст окна блокировки (только из tnovapi.json).</summary>
        [JsonIgnore] public string BlockMessage { get; set; }
        /// <summary>ChecklistStorage из tnovapi.json до наложения локального значения.</summary>
        [JsonIgnore] public string ServerChecklistStorage { get; set; }

        /// <summary>
        /// Шары офисов со старыми версиями плагина для синхронизации файлов (FileSync; только из
        /// tnovapi.json). null/пусто — одна шара: ServerPath. Не менять элементы: копия конфига поверхностная.
        /// </summary>
        [JsonIgnore] public FileSyncShareConfig[] FileSyncShares { get; set; }

        public TNovConfig Clone() => (TNovConfig)MemberwiseClone();
    }
    /// <summary>
    /// Шара для синхронизации файлов на переходный период: "id" — [a-z0-9-]{1,32}, входит в ключ
    /// состояния filesync-{kind} / {модель}@{id}; "path" — корень, как ServerPath (…\_TNov\).
    /// </summary>
    public sealed class FileSyncShareConfig
    {
        public string Id { get; set; }
        public string Path { get; set; }
    }

    public static class TNovConfigLoad
    {
        private static readonly object _cacheLock = new object();
        private static TNovConfig _cached;
        private static DateTime _cachedStampUtc;
        private static bool _usageWarningShown;

        private static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient", "TNovConfig.json");

        /// <summary>
        /// Конфигурация из локального TNovConfig.json с кэшем в памяти: файл перечитывается,
        /// только если изменилась дата записи (его переписывает TNovClient при смене офиса).
        /// Бросает исключение, если файл не читается. Возвращает копию — вызывающий код может её менять.
        /// </summary>
        private static TNovConfig ReadConfig()
        {
            string path = ConfigPath;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            lock (_cacheLock)
            {
                if (_cached == null || stamp != _cachedStampUtc)
                {
                    TNovConfig config = JsonConvert.DeserializeObject<TNovConfig>(File.ReadAllText(path));
                    if (config == null) throw new InvalidDataException($"Пустой файл конфигурации {path}");
                    _cached = config;
                    _cachedStampUtc = stamp;
                }
                TNovConfig result = _cached.Clone();
                Server.ServerSettings.ApplyTo(result);
                return result;
            }
        }

        /// <summary>Конфигурация без диалогов и без записи в usage (null, если не читается). Для фоновых задач.</summary>
        public static TNovConfig GetCachedConfig()
        {
            try { return ReadConfig(); }
            catch (Exception) { return null; }
        }

        public static TNovConfig LoadConfig()
        {
            try
            {
                return ReadConfig();
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при чтении файла конфигурации: {ex.Message}").ShowDialog();
                return null;
            }
        }
        /// <summary>Чтение конфигурации без диалогов — для пакетного режима (TNovAuto).</summary>
        public static bool TryLoadConfig(out TNovConfig config, out string error)
        {
            config = null;
            error = null;
            string configPath = ConfigPath;
            try
            {
                config = ReadConfig();
                if (string.IsNullOrWhiteSpace(config.ServerPath))
                {
                    error = $"В {configPath} не задан ServerPath";
                    return false;
                }
                string blocked = PluginVersionGate.BlockReason(config);
                if (blocked != null)
                {
                    error = blocked.Replace("\n\n", " ");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"Ошибка чтения {configPath}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Строка журнала запусков в формате usage.txt: «дата,пользователь,модель,команда,версия».
        /// Запись уходит в <see cref="Server.ServerOutbox"/> и дописывается на сервер в фоне.
        /// </summary>
        public static void LogUsage(string className, string version)
        {
            Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            DateTime dateTime = DateTime.Now;
            string date = dateTime.ToString(); date = date.Replace(",", "");

            Server.ServerOutbox.AppendLine("usage.txt", date + "," + userName + "," + docName + "," + className + "," + version);
        }

        public static TNovConfig LoadConfig(string className, string version)
        {
            // Автоконтекст справки. Поднимаем ДО основного try: его catch показывает
            // пользователю «Ошибка при чтении файла конфигурации» и возвращает null,
            // и любой сбой справки выглядел бы как отказ конфигурации.
            try
            {
                string helpKey = Help.HelpContextMap.ResolveKey(className);
                if (helpKey != null)
                    Help.HelpPaneHost.SetSection(helpKey);
            }
            catch (Exception) { }

            TNovConfig config;
            try
            {
                config = ReadConfig();
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при чтении файла конфигурации: {ex.Message}").ShowDialog();
                return null;
            }

            //запись в файл usage (при любом типе лицензии) — в фоне, см. ServerOutbox
            try { LogUsage(className, version); } catch (Exception) { }

            // Плагин заблокирован сервером (tnovapi.json). Кнопки на ленте уже неактивны
            // (Application.ApplyPluginBlock) — это страховка для запуска в обход ленты.
            // Возврат null команду не остановит (большинство вызовов его не проверяют), поэтому
            // исключение: Revit покажет его текст в своём окне ошибки внешней команды.
            string blocked = PluginVersionGate.BlockReason(config);
            if (blocked != null && !PluginVersionGate.IsAllowedWhenBlocked(className))
                throw new PluginBlockedException(blocked);

            // Устаревшая версия: предупреждаем при каждом запуске, но команду не блокируем.
            string outdated = PluginVersionGate.OutdatedMessage(config);
            if (outdated != null)
                new InfoWindow280(outdated).ShowDialog();

            // Раньше пользователь сразу видел ошибку записи в usage. Теперь запись отложенная,
            // поэтому предупреждаем один раз за сессию, если фоновая дозапись не проходит.
            string outboxError = Server.ServerOutbox.LastError;
            if (outboxError != null && !_usageWarningShown)
            {
                _usageWarningShown = true;
                new InfoWindow280($"Ошибка добавлении записи о запуске: {outboxError}. " +
                    $"Рекомендуем проверить подключение к папке {config.ServerPath} и перезапустить плагин.").ShowDialog();
            }

            return config;
        }
    }
}
