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
                var id = failureMessageAccessor.GetFailureDefinitionId();
                var failureSeverity = accessor.GetSeverity();
                if (failureSeverity == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failureMessageAccessor);
                }
                else
                {
                    return FailureProcessingResult.Continue;
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
