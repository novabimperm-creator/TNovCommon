using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace TNovCommon
{
    public class WarningSkipper : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            IList<FailureMessageAccessor> failures = accessor.GetFailureMessages();
            foreach (FailureMessageAccessor failureMessageAccessor in failures)
            {
                //серьезность конкретного сообщения (accessor.GetSeverity() - наибольшая по всем сообщениям)
                if (failureMessageAccessor.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failureMessageAccessor);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
