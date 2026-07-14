using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TNovCommon
{
    /// <summary>
    /// Логика взаимодействия для JournalWPF.xaml
    /// </summary>
    public partial class JournalWPF : Window
    {
        private UIDocument _uidoc;
        private Document _doc;
        public JournalWPF(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            var control = new ChecklistControl(_doc);
            FunctionContent.Content = control;
            HighlightButton(BtnChecklist);
        }

        private void BtnChecklist_Click(object sender, RoutedEventArgs e)
        {
            var control = new ChecklistControl(_doc);
            FunctionContent.Content = control;
            HighlightButton(BtnChecklist);
            SetStartMode();
        }

        private void SetStartMode()
        {
        }

        public void SetOwner(System.IntPtr ownerHwnd)
        {
            new WindowInteropHelper(this).Owner = ownerHwnd;
        }

        private void BtnSyncJournal_Click(object sender, RoutedEventArgs e)
        {
            var control = new SyncJournalControl(_doc);
            FunctionContent.Content = control;
            HighlightButton(BtnSyncJournal);
            SetStartMode();
        }

        private void BtnAutoCheck_Click(object sender, RoutedEventArgs e)
        {
            var control = new AutoCheckControl(_doc);
            FunctionContent.Content = control;
            HighlightButton(BtnAutoCheck);
            SetStartMode();
        }

        private void BtnTasks_Click(object sender, RoutedEventArgs e)
        {
            var control = new TasksControl();
            control.RefreshData();
            FunctionContent.Content = control;
            HighlightButton(BtnTasks);
            SetStartMode();
        }

        private void HighlightButton(Button activeButton)
        {
            var buttons = new[] { BtnChecklist, BtnAutoCheck, BtnSyncJournal, BtnTasks };
            var selectedBrush = (SolidColorBrush)FindResource("SelectedBrush");

            foreach (var btn in buttons)
            {
                btn.Background = Brushes.Transparent;
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(Button.BorderThicknessProperty);
            }

            activeButton.Background = selectedBrush;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow((DependencyObject)sender);
            window?.Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = HelpLinks.GetHelpLink("Журнал проекта");
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
}
