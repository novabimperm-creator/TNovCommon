using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.Attributes;

namespace TNovCommon
{
    [Transaction(TransactionMode.Manual)]
    public class TNovWorksetUpdater : IUpdater
    {
        private const string UpdaterName = "TNovWorksetUpdater";

        static readonly TimeSpan ConfigCheckInterval = TimeSpan.FromSeconds(60);
        static DateTime _configCheckedUtc = DateTime.MinValue;
        static bool _isCorp;

        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovWorksetUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "71274837-12b3-48de-a7b8-347600158bb3"));
        }

        /// <summary>
        /// Точка входа Revit. Наружу не должно вылетать ни одного исключения:
        /// любое исключение из IUpdater.Execute Revit показывает пользователю
        /// с предложением отключить обновитель.
        /// </summary>
        public void Execute(UpdaterData data)
        {
            try
            {
                ExecuteCore(data);
            }
            catch (Exception ex)
            {
                UpdaterDiagnostics.Report(UpdaterName, "Execute", ex);
            }
        }

        private void ExecuteCore(UpdaterData data)
        {
            if (data == null) return;

            Document doc = data.GetDocument();
            if (doc == null || doc.IsFamilyDocument) return;
            //проверка файла на наличие наборов
            if (!doc.IsWorkshared) return;

            if (!IsCorpDocument()) return;

            List<Workset> worksets = new FilteredWorksetCollector(doc)  //рабочие наборы документа
                                .Cast<Workset>()                   //элементы категории Рабочие наборы
                                .ToList();                         //формируем список

            WorksetDefaultVisibilitySettings defaultVisibility = null;
            foreach (var workset in worksets)
            {
                try
                {
                    bool isActive = workset.IsVisibleByDefault;
                    if (workset.Kind == WorksetKind.UserWorkset && !isActive)
                    {
                        if (defaultVisibility == null)
                            defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                        defaultVisibility.SetWorksetVisibility(workset.Id, true);
                    }
                }
                catch (Exception ex)
                {
                    UpdaterDiagnostics.Report(UpdaterName, "рабочий набор " + workset.Id, ex);
                }
            }
        }

        /// <summary>
        /// Конфигурация читается с диска, поэтому результат кэшируется:
        /// обновитель вызывается на любое изменение модели.
        /// </summary>
        private static bool IsCorpDocument()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _configCheckedUtc < ConfigCheckInterval) return _isCorp;
            _configCheckedUtc = nowUtc;

            try
            {
                TNovConfig config = TNovConfigLoad.LoadConfig();
                _isCorp = config != null && config.CorpName == "ООО ПМ Новация";
            }
            catch
            {
                _isCorp = false;
            }
            return _isCorp;
        }

        public string GetAdditionalInformation()
        {
            return "TNov, bim@pm-nova.ru";
        }

        public ChangePriority GetChangePriority()
        {
            return ChangePriority.FloorsRoofsStructuralWalls;
        }

        public UpdaterId GetUpdaterId()
        {
            return _updaterId;
        }

        public string GetUpdaterName()
        {
            return UpdaterName;
        }
    }
}
