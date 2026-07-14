using System.Windows;

namespace TNovCommon
{
    /// <summary>
    /// Логика взаимодействия для InfoWindow400.xaml
    /// </summary>
    public partial class InfoWindow400 : Window
    {
        public InfoWindow400(string txt)
        {
            InitializeComponent();
            text1.Text += txt;
            this.SizeToContent = SizeToContent.Height;
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }
    }
}
