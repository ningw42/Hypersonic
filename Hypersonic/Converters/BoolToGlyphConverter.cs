using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class BoolToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isTrue = value is bool b && b;
        if (parameter is string glyphs)
        {
            var parts = glyphs.Split('|');
            if (parts.Length == 2)
            {
                return isTrue ? parts[0] : parts[1];
            }
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
