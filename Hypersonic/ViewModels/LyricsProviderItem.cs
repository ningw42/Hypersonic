using CommunityToolkit.Mvvm.ComponentModel;

namespace Hypersonic.ViewModels;

/// <summary>One row in the lyrics provider settings list.</summary>
public partial class LyricsProviderItem : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }

    /// <summary>Two-way bound to the row checkbox.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>False locks the checkbox — used to forbid disabling the last enabled provider.</summary>
    [ObservableProperty]
    public partial bool CanToggle { get; set; }

    public LyricsProviderItem(string id, string displayName, bool isEnabled)
    {
        Id = id;
        DisplayName = displayName;
        IsEnabled = isEnabled;
        CanToggle = true;
    }
}
