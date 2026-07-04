namespace Hypersonic.Models;

/// <summary>
/// Shared options for a search-based external lyrics provider. <see cref="Enabled"/> mirrors
/// <c>AmllLyricsOptions</c>; <see cref="MatchThreshold"/> (0–100) is the confidence floor below which
/// a fuzzy match is rejected as <c>DefinitiveEmpty</c>.
/// </summary>
public abstract class ExternalSearchLyricsOptions
{
    public bool Enabled { get; set; } = true;
    public int MatchThreshold { get; set; } = 55;
}

public sealed class QQLyricsOptions : ExternalSearchLyricsOptions;
public sealed class NeteaseLyricsOptions : ExternalSearchLyricsOptions;
public sealed class KugouLyricsOptions : ExternalSearchLyricsOptions;
