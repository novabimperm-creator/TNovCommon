using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();
            List<FailureMessageAccessor> failureMessageAccessorList = new List<FailureMessageAccessor>();
            foreach (FailureMessageAccessor failureMessageAccessor in (IEnumerable<FailureMessageAccessor>)failureMessages)
            {
                if (failureMessageAccessor.GetSeverity() == (FailureSeverity)1)
                    failuresAccessor.DeleteWarning(failureMessageAccessor);
                else if (failureMessageAccessor.HasResolutions())
                    failuresAccessor.ResolveFailure(failureMessageAccessor);
                else
                    failureMessageAccessorList.Add(failureMessageAccessor);
            }
            foreach (FailureMessageAccessor failureMessageAccessor in failureMessageAccessorList)
            {
                ICollection<ElementId> failingElementIds = failureMessageAccessor.GetFailingElementIds();
                failuresAccessor.DeleteElements((IList<ElementId>)failingElementIds.ToList<ElementId>());
            }
            return (FailureProcessingResult)0;
        }
    }
}
