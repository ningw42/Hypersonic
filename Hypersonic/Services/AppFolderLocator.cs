using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;

using Windows.Storage;

namespace Hypersonic.Services;

/// <summary>
/// Production <see cref="IAppFolderLocator"/>. The single place under the cache
/// subsystem (and its consumers) that branches on <see cref="RuntimeHelper.IsMSIX"/>
/// for cache folders — the MSIX branch the deleted <c>ImageCachePath</c> used to own
/// now lives here.
/// </summary>
public sealed class AppFolderLocator : IAppFolderLocator
{
    public string GetCacheFolder()
    {
        if (RuntimeHelper.IsMSIX)
        {
            // %LOCALAPPDATA%\Packages\<package family name>\LocalCache
            // %LOCALAPPDATA%\Packages\Hypersonic_257ke566bd2wr\LocalCache
            return ApplicationData.Current.LocalCacheFolder.Path;
        }

        // %LOCALAPPDATA%\Hypersonic\Cache
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypersonic",
            "Cache");
    }
}
