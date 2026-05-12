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
        static AddInId _appId;
        static UpdaterId _updaterId;

        public TNovWorksetUpdater(AddInId id)
        {
            _appId = id;

            _updaterId = new UpdaterId(_appId, new Guid(
                                                   "71274837-12b3-48de-a7b8-347600158bb3"));
        }

        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            //проверка файла на наличие наборов
            bool dws = doc.IsWorkshared;

            TNovConfig config = TNovConfigLoad.LoadConfig();

            if (config != null && config.CorpName == "ООО ПМ Новация" && dws)
            {
                List<Workset> worksets = new FilteredWorksetCollector(doc)  //рабочие наборы документа
                                    .Cast<Workset>()                   //элементы категории Рабочие наборы
                                    .ToList();                         //формируем список
                foreach (var workset in worksets)
                {
                    bool isActive = workset.IsVisibleByDefault;
                    if (workset.Kind == WorksetKind.UserWorkset && !isActive)
                    {
                        WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                        defaultVisibility.SetWorksetVisibility(workset.Id, true);
                    }
                }
            }
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
            return "TNovWorksetUpdater";
        }
    }
}
