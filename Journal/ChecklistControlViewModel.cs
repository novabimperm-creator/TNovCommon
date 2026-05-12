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
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace TNovCommon
{
    public class ChecklistControlViewModel : ObservableObject
    {
        private readonly string _jsonPath;
        private DispatcherTimer _timer;
        private bool _isEditing = false;

        public ObservableCollection<CheckItem> Items { get; set; }
        public ICollectionView ItemsView { get; set; }
        public ObservableCollection<string> Creators { get; } = new ObservableCollection<string>();

        private string _selectedCreator = "Все";
        public string SelectedCreator
        {
            get => _selectedCreator;
            set { if (SetProperty(ref _selectedCreator, value)) ApplyFilter(); }
        }

        private string _newTaskText;
        public string NewTaskText
        {
            get => _newTaskText;
            set { _newTaskText = value; OnPropertyChanged(); }
        }

        public RelayCommand2 AddCommand { get; }
        public RelayCommand2 RemoveCommand { get; }
        public RelayCommand2 EditTitleCommand { get; }
        public RelayCommand2 PastePhotoCommand { get; }
        public RelayCommand2 DeletePhotoCommand { get; }
        public RelayCommand2 ViewPhotoCommand { get; }

        private string PhotosRootFolder
        {
            get
            {
                string folder = Path.Combine(Path.GetDirectoryName(_jsonPath),
                                             Path.GetFileNameWithoutExtension(_jsonPath) + "_photos");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public ChecklistControlViewModel(Document doc)
        {
            _jsonPath = JsonDataService.GetJsonPath(doc, "checklist");

            try
            {
                var data = JsonDataService.Load(_jsonPath);
                foreach (var item in data)
                {
                    if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                    item.SetPhotosRootFolder(PhotosRootFolder);
                    SubscribeItem(item);
                }
                Items = new ObservableCollection<CheckItem>(data);
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Не удалось загрузить данные: {ex.Message}").ShowDialog();
                Items = new ObservableCollection<CheckItem>();
            }

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.SortDescriptions.Add(new SortDescription("IsChecked", ListSortDirection.Ascending));
            ItemsView.SortDescriptions.Add(new SortDescription("CreationDate", ListSortDirection.Descending));
            ApplyFilter();

            Items.CollectionChanged += (s, e) => UpdateCreators();
            UpdateCreators();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += (s, e) => CheckForUpdates();
            _timer.Start();

            // В конструкторе команды AddCommand и RemoveCommand
            AddCommand = new RelayCommand2(_ =>
            {
                if (!string.IsNullOrWhiteSpace(NewTaskText))
                {
                    _isEditing = true;
                    string userName = RevitAPI.UiApplication.Application.Username;

                    var newItem = new CheckItem
                    {
                        Title = NewTaskText,
                        IsChecked = false,
                        Creator = userName,
                        CreationDate = DateTime.Now
                    };
                    newItem.SetPhotosRootFolder(PhotosRootFolder);
                    SubscribeItem(newItem);
                    Items.Add(newItem);
                    ItemsView.Refresh(); // принудительное обновление фильтрации
                    NewTaskText = string.Empty;
                    SaveData();
                    _isEditing = false;
                }
            });

            RemoveCommand = new RelayCommand2(obj =>
            {
                if (obj is CheckItem item)
                {
                    var qViewModel = new QuestionWindowViewModel();
                    qViewModel.headtxt = "Элемент можно отметить выполненным. Действительно удалить элемент?";
                    var qwpfview = new QuestionWindow280(qViewModel);
                    qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                    bool? qok = qwpfview.ShowDialog();
                    if (qok == true)
                    {
                        _isEditing = true;
                        string itemPhotoFolder = Path.Combine(PhotosRootFolder, item.Id.ToString());
                        if (Directory.Exists(itemPhotoFolder))
                        {
                            try { Directory.Delete(itemPhotoFolder, true); }
                            catch { /* игнорируем */ }
                        }
                        item.PropertyChanged -= Item_PropertyChanged;
                        Items.Remove(item);
                        ItemsView.Refresh(); // принудительное обновление фильтрации
                        SaveData();
                        _isEditing = false;
                    }
                }
            });

            EditTitleCommand = new RelayCommand2(obj =>
            {
                if (obj is CheckItem item)
                {
                    // Создаём ViewModel с нужными параметрами
                    var viewModel = new InfoWindowTextFieldViewModel
                    {
                        headtxt = "Введите новое название замечания:",
                        ids = item.Title,              // начальное значение
                        lowtxt = ""                    // при необходимости можно добавить подсказку
                    };

                    // Создаём и показываем окно
                    var window = new InfoWindowTextField(viewModel);
                    if (window.ShowDialog() == true)   // true – если нажали "Хорошо"
                    {
                        string newTitle = viewModel.ids;   // берём изменённое значение
                        if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != item.Title)
                        {
                            item.Title = newTitle;
                            SaveData();
                        }
                    }
                }
            });

            // Команда вставки фото из буфера обмена
            PastePhotoCommand = new RelayCommand2(obj =>
            {
                if (obj is CheckItem item)
                {
                    if (string.IsNullOrEmpty(PhotosRootFolder))
                    {
                        new InfoWindow280("Не задана корневая папка для фотографий.").ShowDialog();
                        return;
                    }

                    try
                    {
                        // Получаем изображение из буфера обмена (через Windows Forms)
                        if (!System.Windows.Forms.Clipboard.ContainsImage())
                        {
                            new InfoWindow280("Буфер обмена не содержит изображения.").ShowDialog();
                            return;
                        }

                        using (var bitmap = System.Windows.Forms.Clipboard.GetImage())
                        {
                            if (bitmap == null)
                            {
                                new InfoWindow280("Не удалось извлечь изображение из буфера обмена.").ShowDialog();
                                return;
                            }

                            string itemFolder = Path.Combine(PhotosRootFolder, item.Id.ToString());
                            Directory.CreateDirectory(itemFolder);

                            string newFileName = $"{Guid.NewGuid()}.png";
                            string newPath = Path.Combine(itemFolder, newFileName);

                            // Сохраняем напрямую в PNG через System.Drawing
                            bitmap.Save(newPath, ImageFormat.Png);

                            // Удаляем старый файл, если был
                            string oldPath = null;
                            if (!string.IsNullOrEmpty(item.PhotoFileName))
                            {
                                oldPath = Path.Combine(itemFolder, item.PhotoFileName);
                            }

                            item.PhotoFileName = newFileName;
                            SaveData();
                            ItemsView.Refresh();

                            if (oldPath != null && File.Exists(oldPath))
                            {
                                try { File.Delete(oldPath); }
                                catch (Exception ex) { /* Логируем */ }
                            }
                        }
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        new InfoWindow280($"Ошибка размеров изображения: {ex.Message}\nПопробуйте скопировать изображение в другом формате.").ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        new InfoWindow280($"Не удалось вставить фото: {ex.Message}").ShowDialog();
                    }
                }
            });
            // Команда удаления фото (исправлена – теперь файл не заблокирован)
            DeletePhotoCommand = new RelayCommand2(obj =>
            {
                if (obj is CheckItem item && !string.IsNullOrEmpty(item.PhotoFileName))
                {
                    try
                    {
                        string itemFolder = Path.Combine(PhotosRootFolder, item.Id.ToString());
                        string filePath = Path.Combine(itemFolder, item.PhotoFileName);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                        item.PhotoFileName = null;
                        SaveData();
                    }
                    catch (Exception ex)
                    {
                        new InfoWindow280($"Не удалось удалить фото: {ex.Message}").ShowDialog();
                    }
                }
            });

            ViewPhotoCommand = new RelayCommand2(obj =>
            {
                if (obj is CheckItem item && !string.IsNullOrEmpty(item.PhotoFullPath) && File.Exists(item.PhotoFullPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(item.PhotoFullPath);
                    }
                    catch (Exception ex)
                    {
                        new InfoWindow280($"Не удалось открыть фото: {ex.Message}").ShowDialog();
                    }
                }
            });
        }

        private void SubscribeItem(CheckItem item) => item.PropertyChanged += Item_PropertyChanged;
        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveData();
            if (e.PropertyName == nameof(CheckItem.Creator))
                UpdateCreators();
        }

        private void ApplyFilter()
        {
            if (ItemsView == null) return;
            ItemsView.Filter = obj =>
            {
                if (obj is CheckItem item)
                {
                    // Если выбран "Все" или фильтр пуст — показываем всё
                    if (string.IsNullOrEmpty(SelectedCreator) || SelectedCreator == "Все")
                        return true;
                    return item.Creator == SelectedCreator;
                }
                return false;
            };
            ItemsView.Refresh();
        }

        private void UpdateCreators()
        {
            // Собираем уникальных создателей из текущих элементов
            var uniqueCreators = Items
                .Select(i => i.Creator)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Обновляем коллекцию для ComboBox
            Creators.Clear();
            Creators.Add("Все");
            foreach (var creator in uniqueCreators)
                Creators.Add(creator);

            // Если выбранный создатель был удалён из списка, сбрасываем на "Все"
            if (!string.IsNullOrEmpty(SelectedCreator) &&
                SelectedCreator != "Все" &&
                !Creators.Contains(SelectedCreator))
            {
                SelectedCreator = "Все";
            }
        }
        private void CheckForUpdates()
        {
            if (_isEditing || string.IsNullOrEmpty(_jsonPath)) return;
            Task.Run(() =>
            {
                try
                {
                    var serverItems = JsonDataService.Load(_jsonPath);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (serverItems.Count != Items.Count)
                        {
                            Items.Clear();
                            foreach (var item in serverItems)
                            {
                                item.SetPhotosRootFolder(PhotosRootFolder);
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
                JsonDataService.Save(_jsonPath, Items.ToList());
            }
            catch (Exception ex)
            {
                new InfoWindow280($"Не удалось сохранить данные: {ex.Message}").ShowDialog();
            }
        }

        /// <summary>
        /// Надёжное получение изображения из буфера обмена.
        /// </summary>
        private BitmapSource GetImageFromClipboard()
        {
            // 1. Проверяем наличие специального формата DIB (используется Paint и др.)
            if (Clipboard.ContainsData(DataFormats.Dib))
            {
                object data = Clipboard.GetData(DataFormats.Dib);
                if (data is MemoryStream ms)
                {
                    try
                    {
                        // Конвертируем DIB в PNG с помощью System.Drawing
                        using (var pngStream = ConvertDibToPng(ms))
                        {
                            if (pngStream != null)
                            {
                                return LoadBitmapSourceFromStream(pngStream);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Ошибка конвертации DIB: {ex.Message}");
                        // Пробуем другие способы
                    }
                }
            }

            // 2. Используем Windows.Forms.Clipboard (более надёжный)
            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                using (var bitmap = System.Windows.Forms.Clipboard.GetImage())
                {
                    if (bitmap != null)
                    {
                        return ConvertBitmapToBitmapSource((Bitmap)bitmap);
                    }
                }
            }

            // 3. Запасной вариант — родной WPF (может сработать для некоторых форматов)
            if (Clipboard.ContainsImage())
            {
                return Clipboard.GetImage();
            }

            return null;
        }

        /// <summary>
        /// Конвертирует System.Drawing.Bitmap в BitmapSource с правильным пиксельным форматом.
        /// </summary>
        private BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            var pixelFormat = ConvertPixelFormat(bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                pixelFormat, null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            bitmapSource.Freeze(); // для безопасного использования в других потоках
            return bitmapSource;
        }

        /// <summary>
        /// Преобразует PixelFormat из System.Drawing в System.Windows.Media.
        /// </summary>
        private System.Windows.Media.PixelFormat ConvertPixelFormat(System.Drawing.Imaging.PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.Format32bppArgb:
                    return PixelFormats.Bgra32;
                case PixelFormat.Format32bppRgb:
                    return PixelFormats.Bgr32;
                case PixelFormat.Format24bppRgb:
                    return PixelFormats.Bgr24;
                case PixelFormat.Format16bppRgb565:
                    return PixelFormats.Bgr565;
                case PixelFormat.Format16bppArgb1555:
                    return PixelFormats.Bgr555;
                case PixelFormat.Format8bppIndexed:
                    return PixelFormats.Gray8;
                case PixelFormat.Format1bppIndexed:
                    return PixelFormats.BlackWhite;
                case PixelFormat.Format16bppGrayScale:
                    return PixelFormats.Gray16;
                default:
                    return PixelFormats.Bgr24; // fallback
            }
        }

        /// <summary>
        /// Сохраняет BitmapSource в PNG файл.
        /// </summary>
        private void SaveBitmapSourceToPng(BitmapSource source, string filePath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }

        /// <summary>
        /// Загружает BitmapSource из потока (PNG, JPEG и т.д.)
        /// </summary>
        private BitmapSource LoadBitmapSourceFromStream(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Конвертирует поток с DIB-форматом в поток с PNG.
        /// </summary>
        private Stream ConvertDibToPng(MemoryStream dibStream)
        {
            // Реализация основана на https://ru.stackoverflow.com/a/1520625
            // (см. ClipboardDibConverter.ConvertToPng)
            // Для краткости здесь приведён упрощённый вариант:
            // читаем DIB, создаём Bitmap, сохраняем как PNG.
            // Полную реализацию можно взять из источника [citation:5].

            // Это заглушка — в реальном коде используйте полную реализацию.
            // Ниже приведён пример с использованием System.Drawing (требуется System.Drawing.Common).
            try
            {
                dibStream.Position = 0;
                using (var bitmap = new Bitmap(dibStream))
                {
                    var pngStream = new MemoryStream();
                    bitmap.Save(pngStream, ImageFormat.Png);
                    pngStream.Position = 0;
                    return pngStream;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
