using Autodesk.Revit.DB.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class FailureAndWarningHandler
    {
        public void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            new WarningSwallower().PreprocessFailures(e.GetFailuresAccessor());
        }
    }
}
