using Microsoft.UI.Xaml.Data;
using System;

namespace Hypersonic.Converters;

public class VolumeToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double volume)
        {
            return volume * 100.0;
        }
        
        return 100.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is double percentage)
        {
            return percentage / 100.0;
        }
        
        return 1.0;
    }
}
