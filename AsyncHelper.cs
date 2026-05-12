using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class AsyncHelper
    {
        // Запускает асинхронную операцию без UI-контекста и синхронно ожидает завершения
        public static T RunSync<T>(Func<Task<T>> asyncFunc)
        {
            return Task.Run(async () => await asyncFunc()).GetAwaiter().GetResult();
        }

        // Перегрузка для операций без результата (сохранение, логирование и т.п.)
        public static void RunSync(Func<Task> asyncFunc)
        {
            Task.Run(async () => await asyncFunc()).GetAwaiter().GetResult();
        }
    }
}
