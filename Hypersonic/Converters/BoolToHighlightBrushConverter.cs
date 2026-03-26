using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Converters;

public class BoolToHighlightBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is true)
        {
            var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"];
            return new SolidColorBrush(accentColor) { Opacity = 0.2 };
        }

        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
