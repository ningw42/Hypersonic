namespace Hypersonic.Services.Cache;

/// <summary>
/// Required surface that every <c>TEntry</c> stored in
/// <see cref="SqliteCacheStoreBase{TEntry}"/> must expose.
///
/// These three fields are what the base needs to do its job
/// (stats, eviction ordering, payload-file resolution):
/// <list type="bullet">
///   <item><see cref="FileName"/> — empty string for negative-cache rows</item>
///   <item><see cref="Length"/> — 0 for negative-cache rows</item>
///   <item><see cref="LastAccessUtc"/> — LRU ordering for eviction</item>
/// </list>
///
/// Tables backing a <c>TEntry</c> must name these three columns exactly
/// <c>FileName</c>, <c>Length</c>, and <c>LastAccessUtc</c> so the base can
/// emit generic <c>SUM(Length)</c>, <c>ORDER BY LastAccessUtc ASC</c>, and
/// <c>WHERE FileName != ''</c> SQL without per-subclass parameterization.
/// </summary>
public interface ICacheEntry
{
    string FileName { get; }

    long Length { get; }

    DateTimeOffset LastAccessUtc { get; }
}
