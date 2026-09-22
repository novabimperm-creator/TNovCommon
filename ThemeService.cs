using Microsoft.Win32;

namespace TNovCommon
{
    public static class ThemeService
    {
        public static bool IsWindowsLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int value)
                        return value != 0;
                }
            }
            catch
            {
            }

            return false;
        }

        public static string GetColorsResourcePath()
        {
            return IsWindowsLightTheme()
                ? "pack://application:,,,/TNovCommon;component/Themes/Colors.Light.xaml"
                : "pack://application:,,,/TNovCommon;component/Themes/Colors.Dark.xaml";
        }
    }
}
