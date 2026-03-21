using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double seconds;

        if (value is double d)
        {
            seconds = d;
        }
        else if (value is int i)
        {
            seconds = i;
        }
        else
        {
            return "0:00";
        }

        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.Hours > 0
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
