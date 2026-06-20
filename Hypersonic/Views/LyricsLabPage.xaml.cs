using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

/// <summary>
/// Throwaway Phase 2 dev harness that hosts <c>LyricsCanvasControl</c> against the live player for the
/// visual smoke test. Deleted in Phase 3 when the control is swapped into <c>NowPlayingPage</c>.
/// </summary>
public sealed partial class LyricsLabPage : Page
{
    public LyricsLabViewModel ViewModel { get; }

    public LyricsLabPage()
    {
        ViewModel = App.GetService<LyricsLabViewModel>();
        InitializeComponent();
    }
}
