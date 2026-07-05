namespace Hypersonic.Core.Models.Lyrics;

/// <summary>One persisted provider row: its id and whether it participates in the fan-out.</summary>
public sealed class LyricsProviderPreference
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
