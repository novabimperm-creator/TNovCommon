using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Path = System.IO.Path;

namespace TNovCommon
{
    public partial class TasksControl : UserControl
    {
        private string _networkPath = "";
        private ObservableCollection<HoleGroupBaseItem> _holeGroups;
        private ICollectionView _holeGroupsView;

        public TasksControl()
        {
            InitializeComponent(); 
            InitializeGrid();
            SetupComboBoxFilters();
            Loaded += ProjectControl_Loaded;
            UpdateUserName();
        }

        private void ProjectControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUserName();
        }

        public void UpdateUserName()
        {
            TNovConfig config = TNovConfigLoad.LoadConfig();
            _networkPath = config.ServerPath;
            if (Directory.Exists(_networkPath))
            {
                string userDepartment = "";
                string userName = UserNameHelper.GetCurrentUserName(true);
                UserNameTextBlock.Text = userName;
                UserNamePrefixTextBlock.Text = "Ваше имя в Revit:";

                string[] rolesFile = File.ReadAllLines($"{config.ServerPath}roles.txt");
                foreach (string role in rolesFile)
                {
                    if (role.Contains(userName))
                    {
                        string[] line = role.Split(',');
                        userDepartment = line[1];
                        break;
                    }
                }
                switch (userDepartment)
                {
                    case "BIM": UserRoleTextBlock.Text = "BIM"; break;
                    case "AR": UserRoleTextBlock.Text = "АР"; break;
                    case "ST": UserRoleTextBlock.Text = "КР"; break;
                    case "VK": UserRoleTextBlock.Text = "ВК"; break;
                    case "OV": UserRoleTextBlock.Text = "ОВ"; break;
                    case "EL": UserRoleTextBlock.Text = "ЭЛ"; break;
                    case "SS": UserRoleTextBlock.Text = "СС"; break;
                    default: UserRoleTextBlock.Text = ""; break;
                }
                if (userDepartment == "") UserRolePrefixTextBlock.Text = "";
                else UserRolePrefixTextBlock.Text = "Ваша роль:";
            }
            else
            {
                UserNamePrefixTextBlock.Text = string.Empty;
                UserNameTextBlock.Text = string.Empty;
                UserRolePrefixTextBlock.Text = string.Empty;
                UserRoleTextBlock.Text = string.Empty;
            }
        }

        private void InitializeGrid()
        {
            _holeGroups = new ObservableCollection<HoleGroupBaseItem>();
            _holeGroupsView = CollectionViewSource.GetDefaultView(_holeGroups);
            _holeGroupsView.Filter = FilterPredicate;
            _holeGroupsView.SortDescriptions.Add(new SortDescription("TaskDate", ListSortDirection.Descending));
            HoleGrid.ItemsSource = _holeGroupsView;
        }

        
        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // Сбрасываем текстовые поля в комбинированных списках
            ProjectComboBox.Text = string.Empty;
            ModelComboBox.Text = string.Empty;

            // Очищаем поля фильтров в заголовках столбцов
            ClearHeaderTextBox("FilterHoleGroup");
            ClearHeaderTextBox("FilterInitiator");
            ClearHeaderTextBox("FilterSTStatus");

            // Обновляем представление данных
            _holeGroupsView?.Refresh();
        }

        private void ClearHeaderTextBox(string name)
        {
            var textBox = FindVisualChild<TextBox>(HoleGrid, name);
            if (textBox != null)
                textBox.Text = string.Empty;
        }
        private bool FilterPredicate(object obj)
        {
            bool isHoleGroupBaseItem = obj is HoleGroupBaseItem;
            if (!isHoleGroupBaseItem) return false;
            HoleGroupBaseItem hole = obj as HoleGroupBaseItem;

            // Фильтр по проекту (текст из ComboBox)
            string projectFilter = ProjectComboBox.Text?.Trim();
            if (!string.IsNullOrEmpty(projectFilter))
            {
                if (hole.ProjectName?.IndexOf(projectFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Фильтр по модели
            string modelFilter = ModelComboBox.Text?.Trim();
            if (!string.IsNullOrEmpty(modelFilter))
            {
                if (hole.ModelName?.IndexOf(modelFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Текстовые фильтры в заголовках
            if (!PassTextFilter(GetFilterText("FilterHoleGroup"), hole.HoleGroupName)) return false;
            if (!PassTextFilter(GetFilterText("FilterInitiator"), hole.Initiator)) return false;
            if (!PassTextFilter(GetFilterText("FilterSTStatus"), hole.STStatus)) return false;

            return true;
        }

        private static bool PassTextFilter(string filterText, string fieldValue)
        {
            if (string.IsNullOrWhiteSpace(filterText)) return true;
            if (string.IsNullOrWhiteSpace(fieldValue)) return false;
            return fieldValue.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetFilterText(string filterName)
        {
            var textBox = FindVisualChild<TextBox>(HoleGrid, filterName);
            return textBox?.Text ?? string.Empty;
        }

        private void Filter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _holeGroupsView?.Refresh();
        }

        private void SetupComboBoxFilters()
        {
            void Attach(ComboBox combo)
            {
                if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox tb)
                {
                    tb.TextChanged += (s, e) => _holeGroupsView?.Refresh();
                }
            }
            ProjectComboBox.Loaded += (s, e) => Attach(ProjectComboBox);
            ModelComboBox.Loaded += (s, e) => Attach(ModelComboBox);
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
        public void RefreshData()
        {
            List<HoleGroupBaseItem> items = GetHoleGroups();
            _holeGroups.Clear();
            foreach (var item in items)
            {
                _holeGroups.Add(item);
            }

            // Обновляем выпадающие списки
            ProjectComboBox.ItemsSource = _holeGroups.Select(x => x.ProjectName).Distinct().OrderBy(x => x).ToList();
            ModelComboBox.ItemsSource = _holeGroups.Select(x => x.ModelName).Distinct().OrderBy(x => x).ToList();

            _holeGroupsView.Refresh();
            TableContainer.Visibility = Visibility.Visible;
        }
        public List<HoleGroupBaseItem> GetHoleGroups()
        {
            TNovConfig config = TNovConfigLoad.LoadConfig();
            string taskFolder = $"{config.ServerPath}tasks/";
            List<HoleGroupBaseItem> tasks = new List<HoleGroupBaseItem>();

            string projectListFile = File.ReadAllText($"{config.ServerPath}CDE.txt");
            string[] lines = projectListFile.Split('\n');
            List<string> projects = new List<string>();
            foreach (string line in lines)
            {
                string[] elems = line.Split(',');
                projects.Add(elems[0]);
            }

            try
            {
                var jsonFiles = GetJsonFiles(taskFolder);
                foreach (var file in jsonFiles)
                {
                    string jsonContent = File.ReadAllText(file);
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                    var existingItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(jsonContent)
                                        ?? new List<HoleGroupBaseItem>();
                    foreach (var item in existingItems)
                    {
                        item.ModelName = fileNameWithoutExtension;
                        foreach (string p in projects)
                        {
                            if (fileNameWithoutExtension.Contains(p))
                            {
                                item.ProjectName = p;
                                break;
                            }
                        }
                        tasks.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }

            return tasks;
        }

        public static List<string> GetJsonFiles(string folderPath, bool recursive = false)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {folderPath}");

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var jsonFiles = Directory.GetFiles(folderPath, "*.json", searchOption).ToList();
            return jsonFiles;
        }
    }

    public static class UserNameHelper
    {
        public static string GetCurrentUserName(bool revitExists)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string iniPath = Path.Combine(appDataPath, "Autodesk", "Revit", "Autodesk Revit 2022", "Revit.ini");

            if (revitExists)
            {
                try
                {
                    string[] lines = File.ReadAllLines(iniPath);
                    bool inPartitionsSection = false;
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed == "[Partitions]")
                            inPartitionsSection = true;
                        else if (inPartitionsSection && trimmed.StartsWith("Username="))
                            return trimmed.Split('=')[1].Trim();
                        else if (trimmed.StartsWith("[") && inPartitionsSection)
                            break;
                    }
                }
                catch { }
            }
            return Environment.UserName;
        }
    }
}