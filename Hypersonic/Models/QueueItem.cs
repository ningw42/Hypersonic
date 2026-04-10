using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Core.Models;

namespace Hypersonic.Models;

public partial class QueueItem : ObservableObject
{
    public PlaybackTrack Track { get; init; } = null!;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
