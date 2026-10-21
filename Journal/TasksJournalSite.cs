using System;
using System.Collections.Generic;

namespace TNovCommon
{
    /// <summary>
    /// Журнал заданий из TNovPRO. Клиент сайта (вход, токены) живёт в TNovTasks,
    /// на который TNovCommon не ссылается, — поэтому загрузчики подставляет TNov
    /// при запуске (TNovTasks.TasksPro.ProJournal.Register). Не подставлены или
    /// вернули null — журнал читается из JSON на сервере, как раньше.
    /// </summary>
    public static class TasksJournalSite
    {
        /// <summary>Задания всех моделей с сайта; null — сайт недоступен или вход не выполнен.</summary>
        public static Func<List<HoleGroupBaseItem>> Loader { get; set; }

        /// <summary>Элементы заданий модели: имя группы → элементы; null — не получены.</summary>
        public static Func<string, Dictionary<string, List<TaskElementRecord>>> ElementsLoader { get; set; }

        internal static List<HoleGroupBaseItem> TryLoad()
        {
            try { return Loader?.Invoke(); }
            catch (Exception ex) { Logger.Log("TNovPRO: журнал заданий не получен — " + ex.Message, 3); return null; }
        }

        internal static Dictionary<string, List<TaskElementRecord>> TryLoadElements(string modelName)
        {
            try { return ElementsLoader?.Invoke(modelName); }
            catch (Exception) { return null; }
        }
    }
}
