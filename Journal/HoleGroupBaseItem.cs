using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class HoleGroupBaseItem
    {
        public string ProjectName { get; set; }
        public string ModelName { get; set; }
        public string JsonFileName { get; set; }
        public string HoleGroupName { get; set; }
        public string HoleGroupNamePart1 { get; set; }
        public string HoleGroupNamePart2 { get; set; }
        public string HoleGroupNamePart3 { get; set; }
        public string TaskVersion { get; set; }
        public string TaskDate { get; set; }
        public string Initiator { get; set; }
        public string STModelName { get; set; }
        public string STStatus { get; set; }
        public string STCheckDate { get; set; }
        public string STMisc { get; set; }
        public string MEPComments { get; set; }
        public string MEPCommentsHistory { get; set; }
        public string IssueHistory { get; set; }
        public List<TaskElementRecord> Elements { get; set; }

        /// <summary>«24 (изм. 3)» — для колонки журнала. Заполняется после загрузки.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string ElementsSummary { get; set; }

        /// <summary>Пометить изменения элементов относительно текущей версии группы.</summary>
        public void PrepareElements()
        {
            if (Elements == null || Elements.Count == 0) { ElementsSummary = "-"; return; }
            int changed = TaskElementRecord.MarkChanges(Elements, TaskVersion);
            int active = Elements.Count(e => e.State != TaskElementRecord.StateRemoved);
            ElementsSummary = changed > 0 ? active + " (изм. " + changed + ")" : active.ToString();
        }
    }
}
