using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TNovCommon
{
    public class InfoWindowTextFieldViewModel : INotifyPropertyChanged
    {
        private string _headtxt;
        private string _ids;
        private string _lowtxt;

        public string headtxt
        {
            get => _headtxt;
            set
            {
                if (_headtxt != value)
                {
                    _headtxt = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ids
        {
            get => _ids;
            set
            {
                if (_ids != value)
                {
                    _ids = value;
                    OnPropertyChanged();
                }
            }
        }

        public string lowtxt
        {
            get => _lowtxt;
            set
            {
                if (_lowtxt != value)
                {
                    _lowtxt = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}