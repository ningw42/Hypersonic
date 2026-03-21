using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.Hours > 0
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }

        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
