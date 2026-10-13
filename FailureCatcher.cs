using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace TNovCommon
{
    /// <summary>
    /// Обработка предупреждений и ошибок при Commit транзакции:
    /// предупреждения пишутся в лог и удаляются, ошибки пишутся в лог и показываются окном Revit.
    /// onError вызывается перед показом окна ошибки (например, чтобы скрыть прогресс-бар, иначе он перекрывает окно).
    /// Элементы не удаляются и не разгруппировываются.
    /// </summary>
    public class FailureCatcher : IFailuresPreprocessor
    {
        private readonly Action onError;

        public FailureCatcher(Action onError = null)
        {
            this.onError = onError;
        }

        /// <summary>Подключает обработчик к транзакции (до Commit)</summary>
        public static void Attach(Transaction transaction, Action onError = null)
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new FailureCatcher(onError));
            transaction.SetFailureHandlingOptions(options);
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool hasErrors = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                bool isWarning = failure.GetSeverity() == FailureSeverity.Warning;
                string ids = String.Join(",", failure.GetFailingElementIds().Select(id => id.ToString()));
                Logger.Log((isWarning ? "Предупреждение Revit: " : "Ошибка Revit: ") + failure.GetDescriptionText()
                    + (ids.Length > 0 ? " Элементы: " + ids : ""), isWarning ? 2 : 4);

                if (isWarning) accessor.DeleteWarning(failure);
                else hasErrors = true;
            }

            if (hasErrors)
            {
                try { onError?.Invoke(); }
                catch (Exception ex) { Logger.Log("Ошибка обработчика ошибок транзакции: " + ex.Message, 4); }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
