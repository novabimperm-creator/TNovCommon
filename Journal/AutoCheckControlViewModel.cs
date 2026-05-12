using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using MessageBox = System.Windows.Forms.MessageBox;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace TNovCommon
{
    public class AutoCheckControlViewModel : ObservableObject
    {
        private readonly string _jsonPath;
        private DispatcherTimer _timer;
        private bool _isEditing = false;

        public ObservableCollection<AutoCheckItem> Items { get; set; }
        public ICollectionView ItemsView { get; set; }
        public ObservableCollection<string> Statuses { get; } = new ObservableCollection<string>();

        private string _selectedStatus = "Все";
        public string SelectedStatus
        {
            get => _selectedStatus;
            set { if (SetProperty(ref _selectedStatus, value)) ApplyFilter(); }
        }

        public RelayCommand2 UpdCommand { get; }
        public RelayCommand2 OpenLogCommand { get; }
        public RelayCommand2 SelectElemsCommand { get; }

        private string LogsRootFolder
        {
            get
            {
                string folder = Path.Combine(Path.GetDirectoryName(_jsonPath),
                                             Path.GetFileNameWithoutExtension(_jsonPath) + "_checklogs");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public AutoCheckControlViewModel(Document doc)
        {
            _jsonPath = JsonDataService.GetJsonPath(doc, "autocheck");

            DateTime dateTime = DateTime.Now;

            try
            {
                var data = JsonDataService.LoadAuto(_jsonPath,dateTime); //получаем список проверок, в т.ч. результаты ранее пройденных
                foreach (var item in data) 
                {
                    item.SetLogsRootFolder(LogsRootFolder);
                    SubscribeItem(item);
                }
                Items = new ObservableCollection<AutoCheckItem>(data);

            }
            catch (Exception ex)
            {
                new InfoWindow280($"Не удалось загрузить данные: {ex.Message}").ShowDialog();
                Items = new ObservableCollection<AutoCheckItem>();
            }

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.SortDescriptions.Add(new SortDescription("Number", ListSortDirection.Ascending));
            ApplyFilter();
            
            Items.CollectionChanged += (s, e) => UpdateStatuses();
            UpdateStatuses();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += (s, e) => CheckForUpdates();
            _timer.Start();

            UpdCommand = new RelayCommand2(_ =>
            {
                //Обновление автоматических проверок (определены в AutoCheckItem)

                foreach (var item in Items) 
                {
                    item.RunAutoCheck();
                    SubscribeItem(item);
                }
                ItemsView.Refresh();
                SaveData();
                _isEditing = false;
            });
            OpenLogCommand = new RelayCommand2(obj => 
            {
                if (obj is AutoCheckItem item)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("notepad.exe", item.LogFullPath + ".txt");
                    }
                    catch (Exception ex)
                    {
                        new InfoWindow400($"Не удалось открыть файл: {ex.Message}").ShowDialog();
                    }
                    ItemsView.Refresh();
                }

            });
            SelectElemsCommand = new RelayCommand2(obj =>
            {
                if (obj is AutoCheckItem item&&item.ElemIds!=null&&item.ElemIds.Length>0)
                {
                    try
                    {
                        UIDocument uidoc = RevitAPI.UiDocument;
                        uidoc.Selection.SetElementIds(item.ElemIds.Split(',').Select(s => new ElementId(int.Parse(s))).ToArray());
                    }
                    catch (Exception e)
                    {
                        new InfoWindow280($"Ошибка: {e.Message}").ShowDialog();
                    }
                    ItemsView.Refresh();
                }
            });
        }

        private void SubscribeItem(AutoCheckItem item) => item.PropertyChanged += Item_PropertyChanged;
        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveData();
            if (e.PropertyName == nameof(CheckItem.Creator))
                UpdateStatuses();
        }

        private void ApplyFilter()
        {
            if (ItemsView == null) return;
            ItemsView.Filter = obj =>
            {
                if (obj is AutoCheckItem item)
                {
                    // Если выбран "Все" или фильтр пуст — показываем всё
                    if (string.IsNullOrEmpty(SelectedStatus) || SelectedStatus == "Все")
                        return true;
                    if (SelectedStatus == "Пройдена")
                        return item.IsChecked == true;
                    if (SelectedStatus == "Не пройдена")
                        return item.IsChecked == false;
                }
                return false;
            };
            ItemsView.Refresh();
        }

        private void UpdateStatuses()
        {
            // Собираем уникальных создателей из текущих элементов
            var uniqueStatuses = Items
                .Select(i => i.IsChecked)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Обновляем коллекцию для ComboBox
            Statuses.Clear();
            Statuses.Add("Все");
            foreach (var status in uniqueStatuses)
            { 
                if(status == false) Statuses.Add("Не пройдена"); else Statuses.Add("Пройдена");
            }

            // Если выбранный создатель был удалён из списка, сбрасываем на "Все"
            if (!string.IsNullOrEmpty(SelectedStatus) &&
                SelectedStatus != "Все" &&
                !Statuses.Contains(SelectedStatus))
            {
                SelectedStatus = "Все";
            }
        }
        private void CheckForUpdates()
        {
            if (_isEditing || string.IsNullOrEmpty(_jsonPath)) return;
            Task.Run(() =>
            {
                try
                {
                    var serverItems = JsonDataService.LoadAuto(_jsonPath, DateTime.Now);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (serverItems.Count != Items.Count)
                        {
                            Items.Clear();
                            foreach (var item in serverItems)
                            {
                                item.SetLogsRootFolder(LogsRootFolder);
                                SubscribeItem(item);
                                Items.Add(item);
                            }
                            // Восстанавливаем фильтр и сортировку
                            ApplyFilter();
                            ItemsView.Refresh();
                        }
                    });
                }
                catch { /* игнорируем ошибки обновления */ }
            });
        }

        public void SaveData()
        {
            try
            {
                JsonDataService.SaveAuto(_jsonPath, Items.ToList());
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Не удалось сохранить данные: {ex.Message}").ShowDialog();
            }
        }

        

        
    }
}
