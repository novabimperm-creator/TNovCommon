using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class FunctionLogger
    {
        public static async Task LogAsync(IDataRepository repo, string functionName, string userName)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            await repo.LogFunctionUsageAsync(functionName, userName, version);
        }

        public static void Log(IDataRepository repo, string functionName, string userName)
        {
            try
            {
                // Запускаем асинхронную операцию в пуле потоков без SynchronizationContext
                Task.Run(() => LogAsync(repo, functionName, userName)).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                // Ошибки логирования не должны влиять на выполнение команды
                System.Diagnostics.Debug.WriteLine($"Ошибка логирования: {ex.Message}");
            }
        }
    }

}
