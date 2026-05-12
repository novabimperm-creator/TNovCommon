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
    }
}
