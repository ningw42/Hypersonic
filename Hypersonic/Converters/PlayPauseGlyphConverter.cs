using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isPlaying && isPlaying)
        {
            return "\uE769"; // Pause
        }

        return "\uE768"; // Play
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
