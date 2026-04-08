using Hypersonic.Models;

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Converters;

public class RepeatModeToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is RepeatMode mode && mode != RepeatMode.Off
            ? "AccentTextFillColorPrimaryBrush"
            : "TextFillColorPrimaryBrush";
        return BoolToAccentBrushConverter.GetThemedBrush(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
