using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Clipboard = System.Windows.Clipboard;

namespace TNovCommon
{
    public partial class InfoWindowTextField : Window
    {
        public InfoWindowTextField(InfoWindowTextFieldViewModel viewModel)
        {
            InitializeComponent();
            textBox1.Focus();
            DataContext = viewModel;
            this.SizeToContent = SizeToContent.Height;
        }

        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close();
        }

        // Неиспользуемый метод escButton_Click можно удалить, т.к. кнопка Cancel не предусмотрена,
        // а IsCancel="True" уже обрабатывает Escape.
        // private void escButton_Click(object sender, RoutedEventArgs e) { ... }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove(); // Позволяет перетаскивать окно за заголовок
        }

        private async void copyButton_Click(object sender, RoutedEventArgs e)
        {
            if (textBox1 == null) return;

            string textToCopy = textBox1.Text;
            if (string.IsNullOrEmpty(textToCopy))
            {
                // По желанию: показать уведомление или просто выйти
                return;
            }

            try
            {
                Clipboard.SetText(textToCopy);

                // Меняем текст кнопки
                string originalContent = copyButton.Content?.ToString() ?? "Скопировать";
                copyButton.Content = "Скопировано!";

                // Возвращаем исходный текст через 2 секунды (необязательно)
                await Task.Delay(2000);
                copyButton.Content = originalContent;
            }
            catch (Exception ex)
            {
                
            }
        }
    }
}
