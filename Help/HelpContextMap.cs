using System;
using System.Collections.Generic;
using System.Linq;

namespace TNovCommon.Help
{
    /// <summary>
    /// Имя функции из журнала запусков (DBCommandName, который команды передают
    /// в TNovConfigLoad.LoadConfig) в ключ реестра HelpLinks.
    ///
    /// Это два разных словаря: журнал различает подфункции («Сваи Автонумерация»,
    /// «Парковки Ручной нумератор»), а справка на них общая. Поэтому:
    ///   1. точное совпадение с ключом реестра,
    ///   2. явный алиас — там, где названия расходятся по существу,
    ///   3. самый длинный ключ реестра, который является префиксом имени
    ///      по границе слова («Сваи Автонумерация» -> «Сваи»).
    /// Неразобранные имена пишутся в расширенный лог — по ним достраивается таблица.
    /// </summary>
    public static class HelpContextMap
    {
        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "О программе",                  "Настройки" },
                { "Оси и уровни в связях",        "Связи проекта" },
                { "Арматура в связях",            "Связи проекта" },
                { "Откреплятор",                  "Закреплятор" },
                { "Задание Автомаркировка",       "Автонумерация заданий" },
                { "Задание Отправить",            "Отправить задание" },
                { "Задание получить",             "Задания" },
                { "Отверстия",                    "Отметки Вырезание" },
                { "Лишние удалить",               "Удалить лишние" },
                { "Менеджер помещений",           "Помещения" },
                { "Номера продаваемых помещений", "Номера помещений" },
                { "Сквозные номера квартир",      "Квартирография" },
                { "Ведомость полов",              "Генератор полов" },
                { "Отделка",                      "Ведомость отделки" }
            };

        /// <summary>Ключи реестра от длинных к коротким — для поиска по префиксу.</summary>
        private static readonly string[] KeysByLength = HelpLinks.All
            .Select(t => t.Key)
            .Where(k => k != "-")
            .OrderByDescending(k => k.Length)
            .ToArray();

        private static readonly HashSet<string> LoggedMisses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Gate = new object();

        /// <summary>Ключ раздела справки или null, если сопоставить не удалось.</summary>
        public static string ResolveKey(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return null;

            string name = commandName.Trim();

            HelpTopic topic;
            if (HelpLinks.TryGetTopic(name, out topic))
                return topic.Key;

            string alias;
            if (Aliases.TryGetValue(name, out alias))
                return alias;

            foreach (string key in KeysByLength)
            {
                if (name.Length <= key.Length)
                    continue;
                if (!name.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Только по границе слова, иначе «Сваи» поймает «Сваиный».
                char next = name[key.Length];
                if (next == ' ' || next == '_' || next == '-')
                    return key;
            }

            LogMissOnce(name);
            return null;
        }

        private static void LogMissOnce(string commandName)
        {
            lock (Gate)
            {
                if (!LoggedMisses.Add(commandName))
                    return;
            }

            try
            {
                Logger.Log("Справка: нет раздела для функции \"" + commandName + "\"", 2);
            }
            catch (Exception)
            {
                // Логи не должны влиять на работу команды.
            }
        }
    }
}
