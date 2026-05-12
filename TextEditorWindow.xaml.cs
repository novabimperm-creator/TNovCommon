using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TNovCommon
{
    public enum DataScope
    {
        User,   // используется function_data
        Model   // используется model_data
    }

    public partial class TextEditorWindow : Window
    {
        private readonly DataService _dataService;
        private readonly DataScope _scope;
        private readonly Guid _userId;        // для User
        private readonly string _modelName;   // для Model
        private readonly string _functionName;

        // Конструктор для пользовательских данных
        public TextEditorWindow(DataService dataService, Guid userId, string functionName)
            : this(dataService, functionName, DataScope.User)
        {
            _userId = userId;
        }

        // Конструктор для данных модели
        public TextEditorWindow(DataService dataService, string modelName, string functionName)
            : this(dataService, functionName, DataScope.Model)
        {
            _modelName = modelName;
        }

        private TextEditorWindow(DataService dataService, string functionName, DataScope scope)
        {
            InitializeComponent();
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _functionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
            _scope = scope;

            Loaded += async (s, e) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                string json = _scope == DataScope.User
                    ? await _dataService.LoadUserDataAsync(_userId, _functionName)
                    : await _dataService.LoadModelDataAsync(_modelName, _functionName);
                JsonTextBox.Text = json;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string json = JsonTextBox.Text;
                if (_scope == DataScope.User)
                    await _dataService.SaveUserDataAsync(_userId, _functionName, json);
                else
                    await _dataService.SaveModelDataAsync(_modelName, _functionName, json);
                MessageBox.Show("Данные сохранены.", "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }
    }
}
