using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

/// <summary>
/// Converts a <see cref="LyricsLoadState"/> into a <see cref="Visibility"/>:
/// returns <c>Visible</c> when the value matches the target state passed via
/// <c>ConverterParameter</c>, otherwise <c>Collapsed</c>.
/// Parameter must be one of: "Loading", "Loaded", "NoLyrics", "NoTrack".
/// </summary>
public class LyricsLoadStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LyricsLoadState state || parameter is not string targetName)
        {
            return Visibility.Collapsed;
        }

        if (!Enum.TryParse<LyricsLoadState>(targetName, out var target))
        {
            return Visibility.Collapsed;
        }

        return state == target ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
