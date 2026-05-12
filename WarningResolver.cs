using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace TNovCommon
{
    public class WarningResolver : IFailuresPreprocessor//...
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            List<ElementId> del = new List<ElementId>();
            IList<FailureMessageAccessor> failures = accessor.GetFailureMessages();
            foreach (FailureMessageAccessor failureMessageAccessor in failures)
            {
                try
                {
                    foreach (ElementId f in failureMessageAccessor.GetAdditionalElementIds())
                    {
                        if (!del.Contains(f))
                        {
                            del.Add(f);
                        }
                    }
                    accessor.DeleteWarning(failureMessageAccessor);

                    if (del.Count > 0)
                    {
                        accessor.DeleteElements(del);
                    }
                }
                catch
                {
                    continue;
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
