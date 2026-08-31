using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace TNovCommon
{
    public partial class JournalWPF : Window
    {
        private readonly string _helpKey;

        public JournalWPF(string title, string helpKey, UserControl content)
        {
            InitializeComponent();
            TitleText.Text = title;
            _helpKey = helpKey;
            FunctionContent.Content = content;
        }

        public void SetOwner(System.IntPtr ownerHwnd)
        {
            new WindowInteropHelper(this).Owner = ownerHwnd;
        }

        public static JournalWPF ShowOwned(string title, string helpKey, UserControl content)
        {
            var window = new JournalWPF(title, helpKey, content);
            try
            {
                window.SetOwner(Process.GetCurrentProcess().MainWindowHandle);
            }
            catch { }
            window.Show();
            return window;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = HelpLinks.GetHelpLink(_helpKey);
            var proc = new Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
}
