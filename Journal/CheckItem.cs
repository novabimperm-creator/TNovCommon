using System;
using System.IO;
using System.Windows.Media.Imaging;
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

        public void SetPhotosRootFolder(string folder)
        {
            _photosRootFolder = folder;
            LoadPhotoImage();
        }

        private void LoadPhotoImage()
        {
            PhotoImageSource = null;
            if (string.IsNullOrEmpty(PhotoFullPath) || !File.Exists(PhotoFullPath)) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(PhotoFullPath);
                bitmap.EndInit();
                bitmap.Freeze();
                PhotoImageSource = bitmap;
                OnPropertyChanged(nameof(PhotoImageSource)); // гарантированное уведомление
            }
            catch { }
        }
    }
}