using Hypersonic.Core.Models;

using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class RepeatModeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.One => "\uE8ED",  // RepeatOne
                _ => "\uE8EE",               // RepeatAll (used for both Off and All)
            };
        }

        return "\uE8EE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
