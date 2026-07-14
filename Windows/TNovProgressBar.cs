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
    }

}
