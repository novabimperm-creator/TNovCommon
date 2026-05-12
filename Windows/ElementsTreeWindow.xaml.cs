using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;

namespace TNovCommon
{
    /// <summary>
    /// Немодальное окно для отображения элементов в древовидной структуре с возможностью выбора и выделения в модели.
    /// </summary>
    public partial class ElementsTreeWindow : Window, INotifyPropertyChanged
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly ObservableCollection<CategoryNode> _categories = new ObservableCollection<CategoryNode>();
        private readonly string _logFilePath;

        public ObservableCollection<CategoryNode> Categories
        {
            get => _categories;
        }

        public ICommand SelectCommand { get; }
        public ICommand CloseCommand { get; }

        public ElementsTreeWindow(UIApplication uiApp, string idsCsv, string TNovclassname,DateTime dateTime,string TNovVersion)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;

            
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = _doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            string date = dateTime.ToString(); date = date.Replace(":", "-");
            //string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string clientFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient");
            string logPath = $"{clientFolderPath}/logs/log,{date},{userName},{docName},{TNovclassname},{TNovVersion}.txt";

            _logFilePath = logPath;
            InitializeComponent();
            DataContext = this;

            SelectCommand = new RelayCommand1(SelectElements);
            CloseCommand = new RelayCommand1(Close);

            Loaded += (s, e) => BuildTree(idsCsv);
        }

        private void BuildTree(string idsCsv)
        {
            try
            {
                var ids = ParseIds(idsCsv);
                var elements = CollectElements(ids);

                // Словарь: CategoryName -> (FamilyName -> (TypeName -> List<ElementId>))
                var categoryDict = new Dictionary<string, Dictionary<string, Dictionary<string, List<ElementId>>>>();

                foreach (var elem in elements)
                {
                    string categoryName = elem.Category?.Name ?? "Без категории";
                    string familyName = GetFamilyName(elem);
                    string typeName = GetTypeName(elem);

                    if (!categoryDict.ContainsKey(categoryName))
                        categoryDict[categoryName] = new Dictionary<string, Dictionary<string, List<ElementId>>>();

                    var familyDict = categoryDict[categoryName];
                    if (!familyDict.ContainsKey(familyName))
                        familyDict[familyName] = new Dictionary<string, List<ElementId>>();

                    var typeDict = familyDict[familyName];
                    if (!typeDict.ContainsKey(typeName))
                        typeDict[typeName] = new List<ElementId>();

                    typeDict[typeName].Add(elem.Id);
                }

                // Построение коллекции узлов с сортировкой на каждом уровне
                var categories = new ObservableCollection<CategoryNode>();
                foreach (var categoryPair in categoryDict.OrderBy(p => p.Key)) // сортировка категорий
                {
                    var catNode = new CategoryNode(categoryPair.Key);
                    var families = categoryPair.Value;

                    foreach (var familyPair in families.OrderBy(p => p.Key)) // сортировка семейств
                    {
                        var famNode = new FamilyNode(familyPair.Key);
                        var types = familyPair.Value;

                        foreach (var typePair in types.OrderBy(p => p.Key)) // сортировка типов
                        {
                            var typeNode = new TypeNode(typePair.Key);
                            var elementIds = typePair.Value;

                            // Сортируем ID по возрастанию (как числа)
                            foreach (var id in elementIds.OrderBy(id => id.IntegerValue))
                            {
                                typeNode.Children.Add(new ElementNode(id));
                            }

                            famNode.Children.Add(typeNode);
                        }

                        catNode.Children.Add(famNode);
                    }

                    categories.Add(catNode);
                }

                // Обновляем основную коллекцию (привязанную к TreeView)
                _categories.Clear();
                foreach (var cat in categories)
                    _categories.Add(cat);
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при построении дерева: {ex.Message}").ShowDialog();
            }
        }

        private IEnumerable<ElementId> ParseIds(string csv)
        {
            return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => int.TryParse(s, out _))
                      .Select(s => new ElementId(int.Parse(s)));
        }

        private IEnumerable<Element> CollectElements(IEnumerable<ElementId> ids)
        {
            var elements = new List<Element>();
            foreach (var id in ids)
            {
                Element elem = _doc.GetElement(id);
                if (elem != null && elem.Category != null)
                    elements.Add(elem);
            }
            return elements;
        }

        private string GetFamilyName(Element elem)
        {
            // Для загружаемых семейств
            if (elem is FamilyInstance fi && fi.Symbol?.Family != null)
                return fi.Symbol.Family.Name;

            // Для системных семейств (стены, перекрытия и т.п.) возвращаем категорию с пометкой
            if (elem.Category != null)
                return $"Системное: {elem.Category.Name}";

            return "Прочее";
        }

        private string GetTypeName(Element elem)
        {
            // Получаем тип элемента (если есть)
            ElementType type = _doc.GetElement(elem.GetTypeId()) as ElementType;
            if (type != null)
                return type.Name;

            // Для элементов без типа (например, виды) используем имя класса
            return elem.GetType().Name;
        }

        private void SelectElements()
        {
            try
            {
                // Собираем все выбранные ID, учитывая иерархию (если родитель отмечен, включаем всех потомков)
                var selectedIds = CollectSelectedIds(_categories).Distinct().ToList();

                if (selectedIds.Any())
                {
                    // Выделяем элементы в модели
                    var uiDoc = _uiApp.ActiveUIDocument;
                    uiDoc.Selection.SetElementIds(selectedIds);

                    // Сворачиваем окно
                    WindowState = WindowState.Minimized;
                }
                else
                {
                    new InfoWindow280("Не выбрано ни одного элемента.").ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Ошибка при выделении элементов: {ex.Message}").ShowDialog();
            }
        }

        private IEnumerable<ElementId> CollectSelectedIds(IEnumerable<CategoryNode> nodes)
        {
            foreach (var cat in nodes)
            {
                if (cat.IsChecked == true)
                {
                    // Если категория отмечена, собираем все ID из неё
                    foreach (var id in GetAllIdsFromCategory(cat))
                        yield return id;
                }
                else
                {
                    // Иначе проверяем подузлы
                    foreach (var id in CollectSelectedIdsFromFamilies(cat.Children))
                        yield return id;
                }
            }
        }

        private IEnumerable<ElementId> CollectSelectedIdsFromFamilies(IEnumerable<FamilyNode> families)
        {
            foreach (var fam in families)
            {
                if (fam.IsChecked == true)
                {
                    foreach (var id in GetAllIdsFromFamily(fam))
                        yield return id;
                }
                else
                {
                    foreach (var id in CollectSelectedIdsFromTypes(fam.Children))
                        yield return id;
                }
            }
        }

        private IEnumerable<ElementId> CollectSelectedIdsFromTypes(IEnumerable<TypeNode> types)
        {
            foreach (var type in types)
            {
                if (type.IsChecked == true)
                {
                    foreach (var elem in type.Children)
                        yield return elem.Id;
                }
                else
                {
                    foreach (var elem in type.Children.Where(e => e.IsChecked == true))
                        yield return elem.Id;
                }
            }
        }

        private IEnumerable<ElementId> GetAllIdsFromCategory(CategoryNode cat)
        {
            foreach (var fam in cat.Children)
                foreach (var id in GetAllIdsFromFamily(fam))
                    yield return id;
        }

        private IEnumerable<ElementId> GetAllIdsFromFamily(FamilyNode fam)
        {
            foreach (var type in fam.Children)
                foreach (var elem in type.Children)
                    yield return elem.Id;
        }

        // Обработка установки родительского окна для немодального режима
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Привязываем к главному окну Revit, чтобы окно не терялось за ним
            var helper = new WindowInteropHelper(this);
            helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //MessageBox.Show(_logFilePath);
            if (string.IsNullOrEmpty(_logFilePath))
            {
                new InfoWindow280("Путь к файлу не указан.").ShowDialog();
                return;
            }

            if (!File.Exists(_logFilePath))
            {
                new InfoWindow400($"Файл не найден: {_logFilePath}").ShowDialog();
            }

            try
            {
                System.Diagnostics.Process.Start("notepad.exe", _logFilePath);
            }
            catch (Exception ex)
            {
                new InfoWindow400($"Не удалось открыть файл: {ex.Message}").ShowDialog();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }

    public class InverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !(bool)value; // Инвертирует значение

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ---------- Модели узлов ----------

    public abstract class CheckableNode : INotifyPropertyChanged
    {
        private bool? _isChecked = false;

        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    if (value.HasValue)
                        SetChildrenChecked(value.Value);
                }
            }
        }

        protected abstract void SetChildrenChecked(bool value);
        protected abstract void UpdateParentChecked();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    public class CategoryNode : CheckableNode
    {
        public string CategoryName { get; }
        public ObservableCollection<FamilyNode> Children { get; set; } = new ObservableCollection<FamilyNode>();

        public CategoryNode(string name)
        {
            CategoryName = name;
        }

        protected override void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
                child.SetCheckedRecursive(value);
        }

        protected override void UpdateParentChecked() { } // Корень дерева, родителя нет
    }

    public class FamilyNode : CheckableNode
    {
        public string FamilyName { get; }
        public ObservableCollection<TypeNode> Children { get; set; } = new ObservableCollection<TypeNode>();

        public FamilyNode(string name)
        {
            FamilyName = name;
        }

        protected override void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
                child.SetCheckedRecursive(value);
        }

        protected override void UpdateParentChecked()
        {
            // Уведомляем родительскую категорию о необходимости пересчитать своё состояние
            // (здесь связь с родителем не хранится, но можно реализовать через события или подписку)
            // Для простоты можно обновлять вручную при изменении, но в данном примере опустим.
        }

        public void SetCheckedRecursive(bool? value)
        {
            IsChecked = value;
            if (value.HasValue)
                foreach (var child in Children)
                    child.SetCheckedRecursive(value.Value);
        }
    }

    public class TypeNode : CheckableNode
    {
        public string TypeName { get; }
        public ObservableCollection<ElementNode> Children { get; set; } = new ObservableCollection<ElementNode>();

        public TypeNode(string name)
        {
            TypeName = name;
        }

        protected override void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
                child.IsChecked = value;
        }

        protected override void UpdateParentChecked()
        {
            // Аналогично
        }

        public void SetCheckedRecursive(bool? value)
        {
            IsChecked = value;
            if (value.HasValue)
                foreach (var child in Children)
                    child.IsChecked = value.Value;
        }
    }

    public class ElementNode : INotifyPropertyChanged
    {
        private bool _isChecked;

        public ElementId Id { get; }
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public ElementNode(ElementId id)
        {
            Id = id;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    // ---------- Вспомогательные классы ----------

    public static class Extensions
    {
        public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
        {
            return new ObservableCollection<T>(source);
        }
    }

    public class RelayCommand1 : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand1(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    
}