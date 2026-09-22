using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TNovCommon
{
    /// <summary>
    /// Раздел справки: ключ функции и страница в вики плагина.
    ///
    /// Изредка справка живёт не статьёй вики, а страницей веб-приложения —
    /// для таких случаев есть directUrl. Он нужен, чтобы адрес оставался здесь,
    /// а не расходился хардкодом по кнопкам ленты и окнам.
    /// </summary>
    public sealed class HelpTopic
    {
        public HelpTopic(string key, string slug, string title, string directUrl = null)
        {
            Key = key;
            Slug = slug;
            Title = title;
            DirectUrl = directUrl;
        }

        /// <summary>Человекочитаемый ключ функции — то, что передают в GetHelpLink.</summary>
        public string Key { get; }

        /// <summary>Слаг страницы в вики плагина. null — страница ещё не перенесена.</summary>
        public string Slug { get; }

        /// <summary>Готовый адрес в обход вики. Имеет приоритет над Slug.</summary>
        public string DirectUrl { get; }

        /// <summary>Заголовок страницы — для дерева в панели справки.</summary>
        public string Title { get; }

        /// <summary>Есть ли у раздела страница в вики плагина.</summary>
        public bool IsMigrated => !string.IsNullOrEmpty(Slug);

        public string Url
        {
            get
            {
                if (!string.IsNullOrEmpty(DirectUrl))
                    return DirectUrl;
                if (!string.IsNullOrEmpty(Slug))
                    return HelpLinks.WikiBase + "#" + Slug;
                return HelpLinks.WikiBase;
            }
        }
    }

    /// <summary>
    /// Единственный реестр ссылок на справку. Ни лента, ни окна не хранят URL сами:
    /// и ContextualHelp кнопок, и кнопки "?" в окнах обращаются сюда.
    /// Переезд вики = правка этого файла.
    /// </summary>
    public static class HelpLinks
    {
        /// <summary>Вики плагина — целевой адрес справки.</summary>
        public const string WikiBase = @"https://tnov.pm-nova.ru/wiki/";


        // Слаг задан           -> страница в вики плагина.
        // Слаг пуст, legacy есть -> страница ещё на старом портале.
        // Оба пусты            -> статьи нет, ведём в корень вики.
        private static readonly HelpTopic[] TopicList =
        {
            new HelpTopic("-", null, "База знаний"),

            // ---- Настройки и общее ----
            new HelpTopic("Настройки", "p-3ece3399b03f", "Настройки"),
            new HelpTopic("Плагины и скрипты", "plaginyiskriptynovatsiya", "Плагины и скрипты (Новация)"),
            new HelpTopic("Старт работы", "startraboty", "Старт работы"),
            new HelpTopic("Вопросы", "p-b3d30c2eb455", "Вопросы TNovPRO"),
            new HelpTopic("Чек-лист", "p-7138d426c07c", "Чек-лист"),
            new HelpTopic("Выбор по ID", "p-80d7446f526a", "Выбор по ID"),
            // Справка по заявкам на семейства — не статья вики, а раздел TNovPRO.
            new HelpTopic("Семейный", null, "Заявки на семейства",
                directUrl: @"https://tnov.pm-nova.ru/?tab=requests"),
            new HelpTopic("Журнал синхронизаций", "p-30b74818946e", "Журнал синхронизаций"),
            new HelpTopic("Журнал заданий", "p-32dae692a608", "Журнал заданий"),

            // ---- Виды и листы ----
            new HelpTopic("Менеджер листов", "p-d9cb02d4547f", "Менеджер листов"),
            new HelpTopic("Изменения", "p-81c6d36efead", "Изменения"),
            new HelpTopic("Экспорт листов", "p-29496f371e8c", "Экспорт листов"),
            new HelpTopic("Excel", "p-5a1a6e2c4e12", "Excel"),

            // ---- Утилиты ----
            new HelpTopic("Закреплятор", "p-61ee7f3c21b7", "Закреплятор Уровни Наборы"),
            new HelpTopic("Типофильтр", "p-6843afec0646", "Типофильтр"),
            new HelpTopic("Связной", "p-a59ee8a7db26", "Связной"),
            new HelpTopic("Связи проекта", "p-9bda11600970", "Связи проекта"),
            new HelpTopic("Перенести", "p-c4e4f64a55e6", "Перенести"),
            new HelpTopic("Закрывашка", null, "Закрывашка"),
            new HelpTopic("Открывашка", null, "Открывашка"),

            // ---- Помещения ----
            new HelpTopic("Помещения", "pomeshcheniya", "Помещения"),
            new HelpTopic("Округлятор", "pomeshcheniya", "Помещения"),
            new HelpTopic("Удалить лишние", "pomeshcheniya", "Помещения"),
            new HelpTopic("Номера по ТЗ", "pomeshcheniya", "Помещения"),
            new HelpTopic("Квартирография", "kvartirografiya", "Квартирография"),
            new HelpTopic("Нумератор квартир", "kvartirografiya", "Квартирография"),
            new HelpTopic("Номера помещений Ручной", "kvartirografiya", "Квартирография"),
            new HelpTopic("Номера помещений", "kladovye", "Кладовые"),
            new HelpTopic("Офисография", "ofisografiya", "Офисография"),
            new HelpTopic("Помещения Резервные копии", "pomeshcheniyarezervnoekopirovanieivosstanovlenie", "Помещения. Резервное копирование и восстановление"),

            // ---- Отделка и АР ----
            new HelpTopic("Генератор полов", "poly", "Полы"),
            new HelpTopic("Ведомость отделки", "vedomostotdelkipomeshcheniy", "Ведомость отделки помещений"),
            new HelpTopic("Антизеркало", "okna", "Окна"),
            new HelpTopic("Проемщик", null, "Проемщик"),
            new HelpTopic("Эт.Номер", "obshchienastroykishablonaar", "Поэтажные спецификации"),
            new HelpTopic("АМ ПСО", null, "АМ ПСО"),
            new HelpTopic("Оформлятор АР", null, "Оформлятор АР"),
            new HelpTopic("Парковки", "parking", "Паркинг"),
            new HelpTopic("Перемычки", "vedomostperemychek", "Ведомость перемычек"),

            // ---- КЖ ----
            new HelpTopic("Сваи", "svai_xmqe", "Сваи"),
            new HelpTopic("Ускорить файл", "uskorenierabotyfaylovmodeli_posu", "Ускорение работы файлов модели"),
            new HelpTopic("Эскизы деталей", "vedomostdetaley", "Ведомость деталей"),
            new HelpTopic("ВРС подчистить", "vedomostraskhodastali", "Ведомость расхода стали"),
            new HelpTopic("Группировка", "skhemaraspolozheniyakonstruktsiy", "Схема расположения конструкций"),
            new HelpTopic("Арматура без марки", "plaginyiskriptynovatsiya", "Плагины и скрипты (Новация)"),

            // ---- Сети ----
            new HelpTopic("Сводная спека", "MEPspec", "Сводная спецификация"),
            new HelpTopic("ADSK Стенки", "MEPductthickness", "Стенки Классы"),
            new HelpTopic("Схемы вентиляции", "MEPviews", "Виды для работы и оформления"),
            new HelpTopic("Схемы ОВ2", "MEPviews", "Виды для работы и оформления"),
            new HelpTopic("Теплопотери", null, "Теплопотери"),
            new HelpTopic("ЭЛ Отметки", "plaginyiskriptynovatsiya", "Плагины и скрипты (Новация)"),
            new HelpTopic("Лотки", null, "Лотки"),
            new HelpTopic("Синхронизатор", null, "Синхронизатор"),
            new HelpTopic("Способы прокладки", null, "Способы прокладки"),
            new HelpTopic("Адресатор", null, "Адресатор"),
            new HelpTopic("Конструктор СС", null, "Конструктор СС"),
            new HelpTopic("Расстановщик СС ПС", null, "Расстановщик СС ПС"),
            new HelpTopic("Проверка пересечений", null, "Проверка пересечений"),

            // ---- Задания ----
            new HelpTopic("Задания", "p-342c19ab1f99", "Порядок отработки заданий конструктором"),
            new HelpTopic("Отметки Вырезание", "p-342c19ab1f99", "Порядок отработки заданий конструктором"),
            new HelpTopic("Отправить задание", "samostoyatelnoemodelirovanieotverstiy", "Отверстия. Согласование, вставка, отслеживание"),
            new HelpTopic("Копировать отверстия", "samostoyatelnoemodelirovanieotverstiy", "Отверстия. Согласование, вставка, отслеживание"),
            new HelpTopic("Автонумерация заданий", "p-32abbff8e44c", "Задания на отверстия и рамы"),

            // ---- BIM ----
            new HelpTopic("BIM Экспорт", null, "Экспорт моделей в Navisworks"),
        };

        private static readonly Dictionary<string, HelpTopic> ByKey = BuildIndex();

        private static Dictionary<string, HelpTopic> BuildIndex()
        {
            var index = new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase);
            foreach (HelpTopic topic in TopicList)
                index[topic.Key] = topic;
            return index;
        }

        /// <summary>Все разделы реестра — для дерева в панели и для валидации слагов.</summary>
        public static IReadOnlyList<HelpTopic> All => TopicList;

        public static bool TryGetTopic(string funcName, out HelpTopic topic)
        {
            topic = null;
            return !string.IsNullOrEmpty(funcName) && ByKey.TryGetValue(funcName, out topic);
        }

        /// <summary>
        /// Адрес справки по ключу функции. Неизвестный ключ ведёт в корень вики.
        /// </summary>
        public static string GetHelpLink(string funcName)
        {
            HelpTopic topic;
            return TryGetTopic(funcName, out topic) ? topic.Url : WikiBase;
        }

        /// <summary>
        /// Открыть справку по функции. Единственная точка: окна не вызывают Process.Start сами.
        /// Если раздел есть в панели справки — показываем панель, иначе уходим в браузер.
        /// </summary>
        public static void ShowHelp(string funcName)
        {
            if (Help.HelpPaneHost.TryShow(funcName))
                return;

            OpenInBrowser(funcName);
        }

        /// <summary>Открыть статью в браузере, минуя панель. Нужно кнопке «Открыть в вики» самой панели.</summary>
        public static void OpenInBrowser(string funcName)
        {
            OpenUrl(GetHelpLink(funcName));
        }

        private static void OpenUrl(string url)
        {
            try
            {
                var info = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(info);
            }
            catch (Exception)
            {
                // Справка не должна ронять команду: если браузер не открылся, молча выходим.
            }
        }
    }
}
