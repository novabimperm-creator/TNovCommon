using System;
using System.Windows;
using System.Windows.Controls;

namespace TNovCommon
{
    public partial class LoginWindow : Window
    {
        public string Upn => UpnTextBox.Text.Trim();
        public string Password => PasswordBox.Password;
        public bool RememberMe => RememberCheckBox.IsChecked == true;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Простейшая проверка заполненности
            if (string.IsNullOrWhiteSpace(Upn))
            {
                ShowError("Введите User Principal Name (например, user@domain.com).");
                UpnTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(Password))
            {
                ShowError("Введите пароль.");
                PasswordBox.Focus();
                return;
            }

            // Диалог завершается успешно
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}