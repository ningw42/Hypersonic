namespace Hypersonic.Services.Cache;

/// <summary>
/// Per-entity cache options consumed by <see cref="SqliteCacheStoreBase{TEntry}"/>.
/// Each entity (image / lyrics / audio) derives its own options class so it can add
/// entity-specific policy knobs (image TTL, audio pinning, etc.) without forcing
/// every entity to share a single options shape.
///
/// <para>
/// The cache parent folder is no longer an option — it is owned authoritatively by
/// <c>IAppFolderLocator.GetCacheFolder()</c>, and the subclass declares only its
/// payload subfolder name (<c>PayloadSubfolderName</c>). Test isolation injects a
/// test locator rather than overriding a root path here.
/// </para>
/// </summary>
public abstract class CacheOptionsBase
{
    /// <summary>
    /// Soft cap on total bytes the cache may hold. The base evicts LRU entries after
    /// each write until total bytes &lt;= this value. Values &lt;= 0 disable eviction.
    /// </summary>
    public long MaximumBytes { get; set; }
}
