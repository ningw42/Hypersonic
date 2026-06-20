namespace Hypersonic.Contracts.Services;

/// <summary>
/// Resolves per-runtime application folders. MSIX vs unpackaged branching lives here
/// so the rest of the codebase stays platform-agnostic. New folder categories land
/// as additional methods on this interface.
/// </summary>
public interface IAppFolderLocator
{
    /// <summary>
    /// Parent folder for cache stores.
    /// MSIX: <c>ApplicationData.Current.LocalCacheFolder.Path</c>.
    /// Unpackaged: <c>%LOCALAPPDATA%\Hypersonic\Cache</c>.
    /// </summary>
    string GetCacheFolder();

    /// <summary>
    /// Folder holding user-sideloaded lyrics overrides (<c>&lt;songId&gt;.ttml</c>). User-managed, not
    /// cache, so it survives a cache clear.
    /// MSIX: <c>ApplicationData.Current.LocalFolder\LyricsOverride</c>.
    /// Unpackaged: <c>%LOCALAPPDATA%\Hypersonic\LyricsOverride</c>.
    /// </summary>
    string GetLyricsOverrideFolder();
}
