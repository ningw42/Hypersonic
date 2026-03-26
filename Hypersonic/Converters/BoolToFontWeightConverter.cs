using Microsoft.UI.Xaml.Data;

using Windows.UI.Text;

namespace Hypersonic.Converters;

public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true
            ? new FontWeight { Weight = 600 }
            : new FontWeight { Weight = 400 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
