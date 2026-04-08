using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Converters;

public class BoolToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is true
            ? "AccentTextFillColorPrimaryBrush"
            : "TextFillColorPrimaryBrush";
        return GetThemedBrush(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    internal static Brush GetThemedBrush(string key)
    {
        // WinUI's XamlControlsResources uses "Default" for dark theme,
        // "Light" for light theme, and "HighContrast" for high contrast.
        var themeKey = "Default";
        if (App.MainWindow.Content is FrameworkElement root
            && root.ActualTheme == ElementTheme.Light)
        {
            themeKey = "Light";
        }

        if (TryResolveThemedBrush(Application.Current.Resources, themeKey, key, out var brush))
        {
            return brush;
        }

        foreach (var merged in Application.Current.Resources.MergedDictionaries)
        {
            if (TryResolveThemedBrush(merged, themeKey, key, out brush))
            {
                return brush;
            }
        }

        return (Brush)Application.Current.Resources[key];
    }

    private static bool TryResolveThemedBrush(
        ResourceDictionary dictionary, string themeKey, string key, out Brush brush)
    {
        brush = null!;
        if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var dict)
            && dict is ResourceDictionary rd
            && rd.TryGetValue(key, out var resource)
            && resource is Brush found)
        {
            brush = found;
            return true;
        }

        return false;
    }
}
