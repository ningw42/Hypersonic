namespace Hypersonic.ViewModels;

/// <summary>A lyrics provider as shown in the settings tokenizing box: a stable id plus its brand display name.</summary>
public sealed class LyricsProviderItem
{
    public string Id { get; }
    public string DisplayName { get; }

    public LyricsProviderItem(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}
