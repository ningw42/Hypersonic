namespace Hypersonic.Services.Cache;

/// <summary>
/// Per-entity cache options consumed by <see cref="SqliteCacheStoreBase{TEntry}"/>.
/// Each entity (image / lyrics / audio) derives its own options class so it can add
/// entity-specific policy knobs (image TTL, audio pinning, etc.) without forcing
/// every entity to share a single options shape.
/// </summary>
public abstract class CacheOptionsBase
{
    /// <summary>
    /// Explicit root path for the entity's cache folder. When null/empty the subclass
    /// resolves a default location via its <c>ResolvePayloadFolder</c> override.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Soft cap on total bytes the cache may hold. The base evicts LRU entries after
    /// each write until total bytes &lt;= this value. Values &lt;= 0 disable eviction.
    /// </summary>
    public long MaximumBytes { get; set; }
}
