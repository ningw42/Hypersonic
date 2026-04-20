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

public sealed partial class ArtistDetailPage : Page
{
    public ArtistDetailViewModel ViewModel { get; }

    public ArtistDetailPage()
    {
        ViewModel = App.GetService<ArtistDetailViewModel>();
        InitializeComponent();
        ActualThemeChanged += OnActualThemeChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += (s, e) =>
        {
            ActualThemeChanged -= OnActualThemeChanged;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
    }

    private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArtistDetailViewModel.SelectedTopSongsMode) or nameof(ArtistDetailViewModel.HasTopSongs))
        {
            SyncTopSongsModeSelector();
        }
    }

    private void SyncTopSongsModeSelector()
    {
        if (!ViewModel.HasTopSongs)
        {
            return;
        }

        var target = ViewModel.SelectedTopSongsMode == 0 ? CommunityModeItem : PersonalModeItem;
        if (TopSongsModeSelector.SelectedItem != target)
        {
            TopSongsModeSelector.SelectedItem = target;
        }
    }

    private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ReRenderAllAlbumCards();
        ReRenderTopSongsRows();
    }

    private void ReRenderAllAlbumCards()
    {
        for (var i = 0; i < ViewModel.Albums.Count; i++)
        {
            var element = DiscographyRepeater.TryGetElement(i);
            if (element is FrameworkElement fe)
            {
                var dc = fe.DataContext;
                fe.DataContext = null;
                fe.DataContext = dc;
            }
        }
    }

    private void ReRenderTopSongsRows()
    {
        for (var i = 0; i < TopSongsListView.Items.Count; i++)
        {
            if (TopSongsListView.ContainerFromIndex(i) is ListViewItem container)
            {
                var dataContext = container.DataContext;
                container.DataContext = null;
                container.DataContext = dataContext;
            }
        }
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AlbumInfo album)
        {
            ViewModel.NavigateToAlbumCommand.Execute(album);
        }
    }

    private void SimilarArtistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ArtistInfo artist)
        {
            ViewModel.NavigateToArtistCommand.Execute(artist);
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
        RenderArtistSegments(rtb, result.Segments, ViewModel.CurrentArtistId);
    }

    private void TopSongsModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selectedItem = sender.SelectedItem;
        if (selectedItem == CommunityModeItem)
        {
            ViewModel.SelectedTopSongsMode = 0;
        }
        else if (selectedItem == PersonalModeItem)
        {
            ViewModel.SelectedTopSongsMode = 1;
        }
        ViewModel.ApplyTopSongsMode();
    }

    private void TopSongsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongInfo song)
        {
            ViewModel.PlayFromTopSongCommand.Execute(song);
        }
    }

    private void TopSongAlbumLink_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
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

    private void TopSongArtistLinks_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        {
            return;
        }

        var result = DisplayArtistRenderer.BuildSegments(
            song.DisplayArtist, song.Artists, song.HasStructuredArtists);
        ToolTipService.SetToolTip(rtb, BuildArtistTooltip(result, song.DisplayArtist, song.Artists));
        RenderArtistSegments(rtb, result.Segments, ViewModel.CurrentArtistId);
    }

    private void RenderArtistSegments(RichTextBlock rtb, List<ArtistSegment> segments, string? excludeArtistId)
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
                if (!string.IsNullOrEmpty(artist.Id) && artist.Id != excludeArtistId)
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
