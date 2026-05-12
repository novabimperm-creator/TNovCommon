using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TNovCommon
{
    public class QuestionWindowViewModel
    {
        public string headtxt { get; set; }


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
