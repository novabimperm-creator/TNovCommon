using System;
using System.Windows;

namespace TNovCommon
{
    public class ThemeColorsDictionary : ResourceDictionary
    {
        public ThemeColorsDictionary()
        {
            MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(ThemeService.GetColorsResourcePath(), UriKind.Absolute)
            });
        }
    }
}
