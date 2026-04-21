using Hypersonic.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += (s, e) =>
        {
            ActualThemeChanged -= OnActualThemeChanged;
        };
    }

    private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;
    private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ReRenderAllCarousels();
    }

    private void ReRenderAllCarousels()
    {
        ReRenderRepeater(RecentlyPlayedRepeater, ViewModel.RecentlyPlayed.Count);
        ReRenderRepeater(RecentlyReleasedRepeater, ViewModel.RecentlyReleased.Count);
        ReRenderRepeater(RecentlyAddedRepeater, ViewModel.RecentlyAdded.Count);
        ReRenderRepeater(MostPlayedRepeater, ViewModel.MostPlayed.Count);
        ReRenderRepeater(RandomPicksRepeater, ViewModel.RandomPicks.Count);
    }

    private static void ReRenderRepeater(ItemsRepeater repeater, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var element = repeater.TryGetElement(i);
            if (element is FrameworkElement fe)
            {
                var dc = fe.DataContext;
                fe.DataContext = null;
                fe.DataContext = dc;
            }
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AlbumInfo album)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, album.Id);
        }
    }

    private void AlbumArtistLinks_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not RichTextBlock rtb || rtb.DataContext is not AlbumInfo album)
        {
            return;
        }

        var result = DisplayArtistRenderer.BuildSegments(
            album.DisplayArtist, album.Artists, album.HasStructuredArtists);
        ToolTipService.SetToolTip(rtb, BuildArtistTooltip(result, album.DisplayArtist, album.Artists));
        RenderArtistSegments(rtb, result.Segments);
    }

    private void RenderArtistSegments(RichTextBlock rtb, List<ArtistSegment> segments)
    {
        rtb.Blocks.Clear();
        var paragraph = new Paragraph();
        var secondaryBrush = SecondaryBrush;
        var tertiaryBrush = TertiaryBrush;

        foreach (var segment in segments)
        {
            if (segment.Kind == ArtistSegmentKind.Connector)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = segment.Text,
                    Foreground = tertiaryBrush,
                });
            }
            else
            {
                var artist = segment.Artist!;
                if (!string.IsNullOrEmpty(artist.Id))
                {
                    var link = new Hyperlink
                    {
                        UnderlineStyle = UnderlineStyle.None,
                        Foreground = secondaryBrush,
                    };
                    link.Inlines.Add(new Run { Text = segment.Text });
                    var artistId = artist.Id;
                    link.Click += (s, e) =>
                    {
                        App.GetService<INavigationService>()
                            .NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId);
                    };
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = segment.Text,
                        Foreground = secondaryBrush,
                    });
                }
            }
        }

        rtb.Blocks.Add(paragraph);
    }

    private static string BuildArtistTooltip(
        BuildSegmentsResult result, string? displayArtist, List<SongArtistInfo> artists)
    {
        if (result.UsedStructuredFallback && !string.IsNullOrEmpty(displayArtist))
        {
            return displayArtist;
        }
        if (result.HasUnmatchedArtists)
        {
            return string.Join(", ", artists.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name));
        }
        return string.Concat(result.Segments.Select(s => s.Text));
    }
}
