using Hypersonic.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Views;

public sealed partial class TracksPage : Page
{
    public TracksViewModel ViewModel { get; }

    public TracksPage()
    {
        ViewModel = App.GetService<TracksViewModel>();
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
        ReRenderAllTrackRowCells();
    }

    private void ReRenderAllTrackRowCells()
    {
        for (var i = 0; i < TrackListView.Items.Count; i++)
        {
            if (TrackListView.ContainerFromIndex(i) is ListViewItem container)
            {
                var dataContext = container.DataContext;
                container.DataContext = null;
                container.DataContext = dataContext;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = ViewModel.OnSearchTextChangedAsync(SearchBox.Text);
    }

    private void ArtistLinks_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        {
            return;
        }

        var result = DisplayArtistRenderer.BuildSegments(
            song.DisplayArtist, song.Artists, song.HasStructuredArtists);
        ToolTipService.SetToolTip(rtb, BuildArtistTooltip(result, song.DisplayArtist, song.Artists));
        RenderArtistSegments(rtb, result.Segments);
    }

    private void AlbumLink_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        {
            return;
        }

        rtb.Blocks.Clear();
        ToolTipService.SetToolTip(rtb, song.Album);
        var secondaryBrush = SecondaryBrush;

        var paragraph = new Paragraph();
        if (!string.IsNullOrEmpty(song.AlbumId))
        {
            var link = new Hyperlink
            {
                UnderlineStyle = UnderlineStyle.None,
                Foreground = secondaryBrush,
            };
            link.Inlines.Add(new Run { Text = song.Album });
            var albumId = song.AlbumId;
            link.Click += (s, e) =>
            {
                App.GetService<INavigationService>()
                    .NavigateTo(typeof(AlbumDetailViewModel).FullName!, albumId);
            };
            paragraph.Inlines.Add(link);
        }
        else
        {
            paragraph.Inlines.Add(new Run
            {
                Text = song.Album,
                Foreground = secondaryBrush,
            });
        }
        rtb.Blocks.Add(paragraph);
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

    private void TrackList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongInfo song)
        {
            ViewModel.PlayFromTrackCommand.Execute(song);
        }
    }
}
