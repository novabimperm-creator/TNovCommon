using Autodesk.Revit.DB;
using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;


namespace TNovCommon
{
    /// <summary>
    /// Логика взаимодействия для TNovProgressBar.xaml
    /// </summary>
    public partial class TNovProgressBar : Window, IComponentConnector
    {
        
        public TNovProgressBar() 
        { 
            this.InitializeComponent(); 
        }
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }
        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close();
        }
        private void closeButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private const double CollapsedOpacity = 0.55;
        private bool _isCollapsed;
        private double _restoreHeight;
        private double _restoreMinHeight;

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCollapsed) ExpandFromStrip();
            else CollapseToStrip();
        }

        // Полоска: прогресс показывает StripBar в шапке (значения привязаны к основному
        // ProgressBar), числа и текст скрыты; ширина окна не меняется.
        private void CollapseToStrip()
        {
            _restoreHeight = ActualHeight;
            _restoreMinHeight = MinHeight;

            info.Visibility = System.Windows.Visibility.Collapsed;
            ProgressRow.Visibility = System.Windows.Visibility.Collapsed;
            TitleText.Visibility = System.Windows.Visibility.Collapsed;
            StripBar.Visibility = System.Windows.Visibility.Visible;
            TitleBar.Margin = new Thickness(6);
            CollapseButton.Content = "v";
            CollapseButton.ToolTip = "Развернуть";

            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            Opacity = IsMouseOver ? 1 : CollapsedOpacity;
            _isCollapsed = true;
        }

        private void ExpandFromStrip()
        {
            SizeToContent = SizeToContent.Manual;
            info.Visibility = System.Windows.Visibility.Visible;
            ProgressRow.Visibility = System.Windows.Visibility.Visible;
            TitleText.Visibility = System.Windows.Visibility.Visible;
            StripBar.Visibility = System.Windows.Visibility.Collapsed;
            TitleBar.Margin = new Thickness(6, 6, 6, 10);
            CollapseButton.Content = "^";
            CollapseButton.ToolTip = "Свернуть в полоску";

            MinHeight = _restoreMinHeight;
            Height = _restoreHeight;
            Opacity = 1;
            _isCollapsed = false;
        }

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isCollapsed) Opacity = 1;
        }

        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isCollapsed) Opacity = CollapsedOpacity;
        }
    }

}
