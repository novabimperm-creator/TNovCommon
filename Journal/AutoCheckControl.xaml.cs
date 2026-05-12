using Autodesk.Revit.DB;
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
    /// Логика взаимодействия для AutoCheckControl.xaml
    /// </summary>
    public partial class AutoCheckControl : UserControl
    {
        public AutoCheckControl(Document doc)
        {
            InitializeComponent();

            // Инициализируем ViewModel, передавая документ
            var vm = new AutoCheckControlViewModel(doc);
            DataContext = vm;

        }
        
    }

}
