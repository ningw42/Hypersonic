using Hypersonic.Core.Models;

using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class RepeatModeToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is RepeatMode mode)
        {
            return mode == RepeatMode.Off ? 0.5 : 1.0;
        }

        return 0.5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
