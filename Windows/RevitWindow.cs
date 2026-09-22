using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;

namespace TNovCommon
{
    /// <summary>Привязывает WPF-окно к главному окну Revit, как ChecklistHost.</summary>
    public static class RevitWindow
    {
        public static bool? ShowDialog(Window window, UIApplication uiapp)
        {
            Attach(window, uiapp);
            return window.ShowDialog();
        }

        public static void Show(Window window, UIApplication uiapp)
        {
            Attach(window, uiapp);
            window.Show();
        }

        public static void Attach(Window window, UIApplication uiapp)
        {
            if (window == null || uiapp == null) return;
            new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
        }
    }
}
