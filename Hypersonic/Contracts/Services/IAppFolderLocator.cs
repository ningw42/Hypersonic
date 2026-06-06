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
}
