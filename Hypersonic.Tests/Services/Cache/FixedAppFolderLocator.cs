using Hypersonic.Contracts.Services;

namespace Hypersonic.Tests.Services.Cache;

/// <summary>
/// Test double for <see cref="IAppFolderLocator"/>. Returns a fixed per-fixture temp
/// directory as the cache parent, replacing the old <c>RootPath</c> test seam. The
/// store composes its payload folder as <c>cacheFolder</c> + <c>PayloadSubfolderName</c>.
/// </summary>
internal sealed class FixedAppFolderLocator : IAppFolderLocator
{
    private readonly string _cacheFolder;

    public FixedAppFolderLocator(string cacheFolder)
    {
        _cacheFolder = cacheFolder;
    }

    public string GetCacheFolder() => _cacheFolder;

    public string GetLyricsOverrideFolder() => Path.Combine(_cacheFolder, "LyricsOverride");
}
