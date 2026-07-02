using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static System.Windows.Forms.LinkLabel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace TNovCommon
{
    public class AutoCheckItem : ObservableObject
    {
        private int _number;
        private bool _isChecked;
        private string _title;
        private DateTime _creationDate;
        private string _creator;
        private string _elemIds;
        private string _logsRootFolder;

        [JsonProperty("title")]
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        [JsonProperty("is_done")]
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        [JsonProperty("created_at")]
        public DateTime CreationDate
        {
            get => _creationDate;
            set
            {
                _creationDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayDate));
            }
        }

        [JsonProperty("creator")]
        public string Creator
        {
            get => _creator;
            set => SetProperty(ref _creator, value);
        }

        [JsonProperty("ids")]
        public string ElemIds
        {
            get => _elemIds;
            set => SetProperty(ref _elemIds, value);
        }
        [JsonProperty]
        public int Number { get => _number; set => SetProperty( ref _number, value); }

        [JsonIgnore]
        public string LogFullPath =>
            !string.IsNullOrEmpty(_logsRootFolder)
                ? Path.Combine(_logsRootFolder, Number.ToString())
                : null;
                
        [JsonIgnore]
        public string DisplayDate => CreationDate.ToString("dd.MM HH:mm");
                
        public void SetLogsRootFolder(string folder)
        {
            _logsRootFolder = folder;
            LoadLog();
        }

        private void LoadLog()
        {
            if (string.IsNullOrEmpty(LogFullPath) || !File.Exists(LogFullPath)) return;

            try
            {
                File.Open(LogFullPath, FileMode.Open, FileAccess.Read);
            }
            catch { }
        }

        public void RunAutoCheck() //Метод запуска проверки, где определены все проверки исходя из их номеров
        {
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string userName = rvtApp.Username;
            Document doc = RevitAPI.Document;

            switch (Number)
            {
                // 0-99 Общие проверки
                case 1:
                    Title = "Оси, уровни и связи закреплены, помещены в свои наборы";
                    string log = ""; int countElems = 0;

                    List<Autodesk.Revit.DB.Grid> grids = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids)      //фильтр по категории Оси
                                                                                     .WhereElementIsNotElementType()    //фильтр только экземпляры
                                                                                     .Cast<Autodesk.Revit.DB.Grid>()    //элементы категории Оси
                                                                                     .ToList();                         //формируем список

                    List<Level> levels = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Levels)   //фильтр по категории Уровни
                                                                     .WhereElementIsNotElementType()    //фильтр только экземпляры
                                                                     .Cast<Level>()                     //элементы категории Уровни
                                                                     .ToList();                         //формируем список

                    List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)      //фильтр по категории Связи
                                                                     .WhereElementIsNotElementType()    //фильтр только экземпляры
                                                                     .Cast<RevitLinkInstance>()         //элементы категории Связи
                                                                     .ToList();                         //формируем список

                    // оси
                    int countGrids = 0; List<string> gridNames = new List<string>(); List<string> gridIds = new List<string>();
                    if (grids.Count > 0) 
                    {
                        foreach (var elem in grids)
                        {
                            if (elem.Pinned == false)
                            {
                                countElems++; countGrids++; gridNames.Add(elem.Name);
#if R2022
                                string idstr = elem.Id.IntegerValue.ToString();
#else
                                string idstr = elem.Id.Value.ToString();
#endif
                                gridIds.Add(idstr);
                            }
                        }
                    }

                    if (countGrids > 0)
                    {
                        log += $"\nОси не закреплены: {String.Join(", ", gridNames)}\nId: {String.Join(", ", gridIds)}\n";
                    }

                    // уровни
                    int countLevels = 0; List<string> levelNames = new List<string>(); List<string> levelIds = new List<string>();
                    if (levels.Count > 0)
                    {
                        foreach (var elem in levels)
                        {
                            if (elem.Pinned == false)
                            {
                                countElems++; countLevels++; levelNames.Add(elem.Name);
#if R2022
                                string idstr = elem.Id.IntegerValue.ToString();
#else
                                string idstr = elem.Id.Value.ToString();
#endif
                                levelIds.Add(idstr);
                            }
                        }
                    }

                    if (countLevels > 0)
                    {
                        log += $"\nУровни не закреплены: {String.Join(", ", levelNames)}\nId: {String.Join(", ", levelIds)}\n";
                    }

                    // связи
                    int countLinks = 0; List<string> linkNames = new List<string>(); List<string> linkIds = new List<string>();
                    if (links.Count > 0)
                    {
                        foreach (var elem in links)
                        {
                            if (elem.Pinned == false)
                            {
                                countElems++; countLinks++; linkNames.Add(elem.Name);
#if R2022
                                string idstr = elem.Id.IntegerValue.ToString();
#else
                                string idstr = elem.Id.Value.ToString();
#endif
                                linkIds.Add(idstr);
                            }
                        }
                    }

                    if (countLinks > 0)
                    {
                        log += $"\nСвязи не закреплены: {String.Join(", ", linkNames)}\nId: {String.Join(", ", linkIds)}\n";
                    }

                    List<string> gridIds1 = new List<string>(); List<string> levelIds1 = new List<string>(); List<string> linkIds1 = new List<string>();
                    if (doc.IsWorkshared)
                    {
                        List<Workset> worksets0 = new FilteredWorksetCollector(doc)  //рабочие наборы документа
                .OfKind(WorksetKind.UserWorkset)
                                         .Cast<Workset>()                   //элементы категории Рабочие наборы
                                         .ToList();                         //формируем список

                        //проверяем наличие набора для осей/уровней
                        List<Workset> worksetsGL = new List<Workset>();
                        foreach (Workset ws in worksets0)
                        {
                            string wname = ws.Name;
                            if (wname == "Общие слои и сетки") worksetsGL.Add(ws);
                            if (wname == "Оси и уровни") worksetsGL.Add(ws);
                            if (wname == "Общие уровни и сетки") worksetsGL.Add(ws);
                        }

                        //оси: набор
                        int countGrids1 = 0; List<string> gridNames1 = new List<string>(); 
                        foreach (var elem in grids)
                        {
                            string workset = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM).AsValueString();
                            if(workset.Contains("сетки")||workset.Contains("Оси") || workset.Contains("оси")) { }
                            else
                            {
                                countElems++; countGrids1++; gridNames1.Add(elem.Name);
#if R2022
                                string idstr = elem.Id.IntegerValue.ToString();
#else
                                string idstr = elem.Id.Value.ToString();
#endif
                                gridIds1.Add(idstr);
                            }
                        }
                        if (countGrids1 > 0)
                        {
                            log += $"\nОси не в своем наборе: {String.Join(", ", gridNames1)}\nId: {String.Join(", ", gridIds1)}\n";
                        }

                        //уровни: набор
                        int countLevels1 = 0; List<string> levelNames1 = new List<string>(); 
                        foreach (var elem in levels)
                        {
                            string workset = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM).AsValueString();
                            if (workset.Contains("слои") || workset.Contains("Уровни") || workset.Contains("уровни")) { }
                            else
                            {
                                countElems++; countLevels1++; levelNames1.Add(elem.Name);
#if R2022
                                string idstr = elem.Id.IntegerValue.ToString();
#else
                                string idstr = elem.Id.Value.ToString();
#endif
                                levelIds1.Add(idstr);
                            }
                        }
                        if (countLevels1 > 0)
                        {
                            log += $"\nУровни не в своем наборе: {String.Join(", ", levelNames1)}\nId: {String.Join(", ", levelIds1)}\n";
                        }

                        //связи: наборы
                        int countLinks1 = 0; List<string> linkNames1 = new List<string>(); 
                        foreach (var link in links)
                        {
                            string lname = link.Name;
                            string[] nameparts = lname.Split(new char[] { ':' });
                            lname = nameparts[0];
                            lname = lname.Replace(".rvt", "");
                            string linkid = link.Id.ToString();
                            int worksetScenario = 2;
                            if (link.Name.Contains("-РФ") || link.Name.Contains("_РФ")) worksetScenario = 0;
                            if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) worksetScenario = 1;

                            string workset = link.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM).AsValueString();
                            Element linkType = doc.GetElement(link.GetTypeId());//тип
                            string typeWorkset = linkType.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM).AsValueString();

                            bool checkedW = false; bool checkedTypeW = false;

                            switch (worksetScenario)
                            {
                                case 0:
                                    checkedW = workset.Contains("слои") || workset.Contains("Уровни") || workset.Contains("уровни");
                                    checkedTypeW = typeWorkset.Contains("слои") || typeWorkset.Contains("Уровни") || typeWorkset.Contains("уровни");
                                    break;
                                case 1:
                                    checkedW = workset.Contains("Задания смежникам");
                                    checkedTypeW = typeWorkset.Contains("Задания смежникам");
                                    break;
                                case 2:
                                    checkedW = workset.Contains(lname);
                                    checkedTypeW = typeWorkset.Contains(lname);
                                    break;
                            }

                            if(checkedW==false||checkedTypeW==false)
                            {
                                countElems++; countLinks1++; linkNames1.Add(lname);
#if R2022
                                string idstr = link.Id.IntegerValue.ToString();
#else
                                string idstr = link.Id.Value.ToString();
#endif
                                linkIds1.Add(idstr);
                            }
                        }
                        if (countLinks1 > 0)
                        {
                            log += $"\nСвязи не в своем наборе: {String.Join(", ", linkNames1)}\nId: {String.Join(", ", linkIds1)}\n";
                        }
                    }

                    File.WriteAllText(LogFullPath + ".txt", log);

                    List<string> allIds = ListMerger.MergeLists(gridIds, levelIds, linkIds, gridIds1, levelIds1, linkIds1);

                    if (allIds.Count > 0)
                    {
                        ElemIds = String.Join(", ", allIds);
                        IsChecked = false;
                    }
                    else { IsChecked = true; ElemIds = ""; }

                    CreationDate = DateTime.Now;
                    
                    break; // Закрепленность и наборы
            }
        }

    }
    public class BaseItems
    {
        public List<int> numbers = new List<int>()  //Список уникальных номеров проверок
        {
            // 0-99 Общие проверки

            1, // Закрепленность и наборы
            
        };
        public List<AutoCheckItem> GetBaseItems(DateTime dateTime)
        {
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string userName = rvtApp.Username;

            List<AutoCheckItem> items = new List<AutoCheckItem>();
            foreach (var num in numbers) 
            {
                items.Add(new AutoCheckItem()
                {
                    Number = num,
                    IsChecked = false,
                    CreationDate = dateTime,
                    Creator = userName
                });
            };

            return items;
        }
    }
    public static class ListMerger
    {
        /// <summary>
        /// Объединяет несколько списков строк в один.
        /// </summary>
        /// <param name="lists">Массив списков для объединения.</param>
        /// <returns>Новый список, содержащий все элементы исходных списков в порядке их перечисления.</returns>
        /// <exception cref="ArgumentNullException">Если передан null или один из списков равен null.</exception>
        public static List<string> MergeLists(params List<string>[] lists)
        {
            // Проверка на null с использованием nameof (C# 6)
            if (lists == null)
                throw new ArgumentNullException(nameof(lists));

            // Проверка каждого списка на null
            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i] == null)
                    throw new ArgumentNullException($"{nameof(lists)}[{i}]", "One of the lists is null.");
            }

            // Объединение с помощью LINQ SelectMany
            return lists.SelectMany(list => list).Distinct().ToList();
        }
    }
}