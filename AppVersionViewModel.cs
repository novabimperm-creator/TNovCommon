using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class AppVersionViewModel : INotifyPropertyChanged
    {
        public string headtxt { get; set; }
        public string url { get; set; }
        private bool _extendedLogs = false;
        public bool extendedLogs
        {
            get => _extendedLogs; set { _extendedLogs = value; OnPropertyChanged(); }
        }
        private bool _canPurge = false;
        public bool canPurge
        {
            get => _canPurge; set { _canPurge = value; OnPropertyChanged(); }
        }
        private bool _canCreateParts = false;
        public bool canCreateParts
        {
            get => _canCreateParts; set { _canCreateParts = value; OnPropertyChanged(); }
        }
        [JsonIgnore] public string userName { get; set; }
        [JsonIgnore] public string userDep { get; set; }
        [JsonIgnore] public string userDepRole { get; set; }

        [JsonIgnore] public ObservableCollection<string> synclist { get; set; }
        private string _sync1;
        public string sync1 { get { return _sync1; } set { _sync1 = value; OnPropertyChanged(); } }
        private int _paramnum = 0;
        public int paramnum { get => _paramnum; set { _paramnum = value; OnPropertyChanged(); } }
        public AppVersionViewModel()
        {
            Param();
        }
        private void Param()
        {
            synclist = new ObservableCollection<string>
            {
                "Подсветка 20/30 минут",
                "Подсветка 30/60 минут",
                "Подсветка 40/60 минут",
                "Подсветка 60/90 минут",
                "Без подсветки панелей (не рекомендуется)",
                "Подсветка 1/2 минуты :-)"
            };
            sync1 = synclist[paramnum];
        }

        public event EventHandler CloseRequest;
        private void RaiseCloseRequest()
        {
            CloseRequest?.Invoke(this, EventArgs.Empty);
        }
        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }
    }
}
