using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound string is non-null
/// and non-whitespace, otherwise <see cref="Visibility.Collapsed"/>.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
