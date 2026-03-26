using CommunityToolkit.Mvvm.ComponentModel;

namespace Hypersonic.Models;

public partial class QueueItem : ObservableObject
{
    public PlaybackTrack Track { get; init; } = null!;

    [ObservableProperty]
    private bool isCurrent;
}
