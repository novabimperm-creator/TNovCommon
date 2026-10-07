using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TNovCommon
{
    /// <summary>
    /// Элемент задания (отверстие, рама, шахта, приямок) в выданной группе.
    /// Пишется в JSON заданий при каждой выдаче (TNovTasks.TaskSend); журналы
    /// (TNovCommon, TNovDesktop — файл подключён ссылкой) только читают.
    /// Элемент отслеживается по паре Марка + ID: если одно из них потерялось,
    /// второе позволяет узнать, каким элемент был в прошлой версии.
    /// </summary>
    public class TaskElementRecord
    {
        public const string StateActive = "в задании";
        public const string StateRemoved = "удалено";
        public const string NoChanges = "-";

        public string Mark { get; set; }
        public long ElementId { get; set; }
        public string Family { get; set; }
        /// <summary>"круглое" / "прямоугольное" / "-"</summary>
        public string Shape { get; set; }

        // Размеры, мм
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double? Diameter { get; set; }
        public double? Length { get; set; }

        // Координаты точки вставки во внутренней системе модели Заданий, мм
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }

        public string State { get; set; }
        /// <summary>Последняя версия задания, в которой элемент был зафиксирован.</summary>
        public string LastVersion { get; set; }
        /// <summary>Последняя версия, в которой у элемента что-то поменялось (не «-»).</summary>
        public string LastChangedVersion { get; set; }
        /// <summary>«v2: -; v3: новое в задании; v4: смещено на +500 мм по Y»</summary>
        public string History { get; set; }

        /// <summary>
        /// Дописать запись «vN: текст» в историю. isChange = false для «-» и служебных
        /// записей (первая фиксация) — такие элементы журнал не подсвечивает.
        /// </summary>
        public void AppendHistory(string version, string text, bool isChange)
        {
            string entry = "v" + version + ": " + text;
            History = string.IsNullOrEmpty(History) ? entry : History + "; " + entry;
            if (isChange) LastChangedVersion = version;
        }

        #region Для журналов (в JSON не пишется)

        /// <summary>Записи истории по одной в строке.</summary>
        [JsonIgnore]
        public List<string> HistoryEntries =>
            string.IsNullOrEmpty(History)
                ? new List<string>()
                : History.Split(new[] { "; v" }, StringSplitOptions.RemoveEmptyEntries)
                         .Select((s, i) => i == 0 ? s : "v" + s)
                         .ToList();

        [JsonIgnore]
        public string SizeText
        {
            get
            {
                if (Diameter.HasValue) return "Ø" + Diameter.Value.ToString("0");
                var parts = new List<string>();
                if (Width.HasValue) parts.Add(Width.Value.ToString("0"));
                if (Height.HasValue) parts.Add(Height.Value.ToString("0"));
                if (Length.HasValue) parts.Add(Length.Value.ToString("0"));
                return parts.Count > 0 ? string.Join("×", parts) : "-";
            }
        }

        [JsonIgnore]
        public bool MarkEmpty => string.IsNullOrWhiteSpace(Mark);

        /// <summary>
        /// Что произошло с элементом в текущей (последней) версии группы:
        /// "new" / "changed" / "removed" / "". Заполняется журналом после загрузки.
        /// </summary>
        [JsonIgnore]
        public string ChangeKind { get; set; } = "";

        /// <summary>Вычислить ChangeKind относительно текущей версии группы.</summary>
        public void UpdateChangeKind(string groupVersion)
        {
            ChangeKind = "";
            if (string.IsNullOrEmpty(groupVersion) || LastChangedVersion != groupVersion) return;
            if (State == StateRemoved) { ChangeKind = "removed"; return; }
            var last = HistoryEntries.LastOrDefault() ?? "";
            ChangeKind = last.Contains("новое в задании") ? "new" : "changed";
        }

        /// <summary>Пометить элементы группы и вернуть число изменённых в последней версии.</summary>
        public static int MarkChanges(IList<TaskElementRecord> elements, string groupVersion)
        {
            if (elements == null) return 0;
            int n = 0;
            foreach (var e in elements)
            {
                e.UpdateChangeKind(groupVersion);
                if (e.ChangeKind.Length > 0) n++;
            }
            return n;
        }

        #endregion
    }
}
