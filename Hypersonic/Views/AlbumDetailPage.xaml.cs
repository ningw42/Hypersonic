using System.ComponentModel;

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

public sealed partial class AlbumDetailPage : Page
{
    public AlbumDetailViewModel ViewModel { get; }

    public AlbumDetailPage()
    {
        ViewModel = App.GetService<AlbumDetailViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += (s, e) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ActualThemeChanged -= OnActualThemeChanged;
        };
    }

    private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;
    private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlbumDetailViewModel.AlbumArtists))
        {
            RenderAlbumHeaderArtists();
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        RenderAlbumHeaderArtists();
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

    private void RenderAlbumHeaderArtists()
    {
        var result = DisplayArtistRenderer.BuildSegments(
            ViewModel.AlbumDisplayArtist, ViewModel.AlbumArtists, ViewModel.AlbumHasStructuredArtists);
        ToolTipService.SetToolTip(AlbumArtistText,
            BuildArtistTooltip(result, ViewModel.AlbumDisplayArtist, ViewModel.AlbumArtists));
        RenderArtistSegments(AlbumArtistText, result.Segments);
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
        else if (e.ClickedItem is DiscHeaderItem header)
        {
            ViewModel.ToggleDiscExpansionCommand.Execute(header);
        }
    }
}
