namespace Hypersonic.ViewModels;

/// <summary>
/// State of the lyrics surface for the Now Playing page.
/// </summary>
public enum LyricsLoadState
{
    /// <summary>No current track — page is in placeholder mode (silent).</summary>
    NoTrack,
    /// <summary>Loading from server.</summary>
    Loading,
    /// <summary>Lyrics text is available.</summary>
    Loaded,
    /// <summary>Both endpoints definitively returned no content for an actual track.</summary>
    NoLyrics,
}
