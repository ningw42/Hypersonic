using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Hypersonic.Converters;

/// <summary>
/// Returns the favorite-red brush when starred, otherwise the secondary text brush.
/// Used by the player bar heart button so the filled heart reads red and the
/// outline heart reads as a muted, neutral icon.
/// </summary>
public class BoolToFavoriteBrushConverter : IValueConverter
{
    // Same red as the track-list FavoriteToggleButtonStyle checked state.
    // Theme-independent, so a single shared brush is fine.
    private static readonly SolidColorBrush FavoriteBrush =
        new(Color.FromArgb(0xFF, 0xE7, 0x48, 0x56));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return FavoriteBrush;
        }

        // Resolve against the player bar's actual theme (not Application.Current
        // .Resources, which doesn't track element-level theme). This is re-run on
        // theme change because PlayerViewModel.NotifyThemeChanged() re-raises IsStarred.
        return BoolToAccentBrushConverter.GetThemedBrush("TextFillColorSecondaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
