using Autodesk.Revit.DB;

namespace TNovCommon
{
    public static class TransactionHandler
    {
        public static void SetWarningResolver(Transaction transaction)
        {
            FailureHandlingOptions failOptions = transaction.GetFailureHandlingOptions();
            failOptions.SetFailuresPreprocessor(new WarningResolver());
            transaction.SetFailureHandlingOptions(failOptions);
        }
    }
}
