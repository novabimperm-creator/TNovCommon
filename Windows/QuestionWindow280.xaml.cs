using System.Windows;

namespace TNovCommon
{
    /// <summary>
    /// Логика взаимодействия для QuestionWindow280.xaml
    /// </summary>
    public partial class QuestionWindow280 : Window
    {
        public QuestionWindow280(QuestionWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.SizeToContent = SizeToContent.Height;
        }
        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close(); // закрытие окна
        }
        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
    }
}
