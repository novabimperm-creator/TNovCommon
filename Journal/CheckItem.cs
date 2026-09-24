using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace TNovCommon
{
    public class CheckItem : ObservableObject
    {
        private Guid _id;
        private bool _isChecked;
        private string _title;
        private DateTime _creationDate;
        private string _creator;
        private string _photoFileName;
        private string _photosRootFolder;
        private BitmapImage _photoImageSource;
        private Dispatcher _dispatcher;
        private int _photoGen;

        [JsonProperty("id")]
        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

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

        [JsonProperty("photo")]
        public string PhotoFileName
        {
            get => _photoFileName;
            set
            {
                if (SetProperty(ref _photoFileName, value))
                {
                    OnPropertyChanged(nameof(PhotoFullPath));
                    LoadPhotoImage();
                }
            }
        }

        /// <summary>Id фото в TNovApi (/api/files). null в файловом режиме — там хватает имени файла.</summary>
        [JsonProperty("photo_file_id", NullValueHandling = NullValueHandling.Ignore)]
        public string PhotoFileId { get; set; }

        [JsonIgnore]
        public string PhotoFullPath =>
            !string.IsNullOrEmpty(PhotoFileName) && !string.IsNullOrEmpty(_photosRootFolder)
                ? Path.Combine(_photosRootFolder, Id.ToString(), PhotoFileName)
                : null;

        [JsonIgnore]
        public BitmapImage PhotoImageSource
        {
            get => _photoImageSource;
            private set => SetProperty(ref _photoImageSource, value);
        }

        [JsonIgnore]
        public string DisplayDate => CreationDate.ToString("dd.MM HH:mm");

        public CheckItem()
        {
            Id = Guid.NewGuid();
        }

        /// <summary>Вызывать в UI-потоке: картинка потом подгружается в фоне и отдаётся в этот поток.</summary>
        public void SetPhotosRootFolder(string folder)
        {
            _photosRootFolder = folder;
            _dispatcher = Dispatcher.CurrentDispatcher;
            LoadPhotoImage();
        }

        /// <summary>Перечитать картинку (фото докачалось в кэш из API). Вызывать в UI-потоке.</summary>
        public void RefreshPhoto()
        {
            OnPropertyChanged(nameof(PhotoFullPath));
            LoadPhotoImage();
        }

        /// <summary>
        /// Фото может лежать на шаре: File.Exists и чтение — в фоне, готовая (замороженная)
        /// картинка передаётся в UI-поток. Без Dispatcher (пункт ещё не показан) — ничего не грузим.
        /// </summary>
        private void LoadPhotoImage()
        {
            string path = PhotoFullPath;
            int gen = Interlocked.Increment(ref _photoGen);
            Dispatcher dispatcher = _dispatcher;
            if (string.IsNullOrEmpty(path) || dispatcher == null)
            {
                PhotoImageSource = null;
                return;
            }

            Task.Run(() =>
            {
                BitmapImage bitmap = null;
                try
                {
                    if (File.Exists(path))
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(path);
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                }
                catch { bitmap = null; }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (gen != Volatile.Read(ref _photoGen)) return; // фото успели сменить
                    PhotoImageSource = bitmap;
                    OnPropertyChanged(nameof(PhotoImageSource)); // гарантированное уведомление
                }));
            });
        }
    }
}