using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// The amll-ttml-db index: a locally-cached, background-refreshed lookup from a track to its raw
/// TTML file name. Single writer; the provider is a pure reader via <see cref="TryResolve"/>.
/// </summary>
public interface IAmllLyricsIndex
{
    /// <summary>True once an index (from disk or network) is loaded and queryable.</summary>
    bool IsReady { get; }

    /// <summary>Resolves a track to its <c>rawLyricFile</c>, or <c>null</c> if not ready or no match.</summary>
    string? TryResolve(LyricsQuery query);

    /// <summary>Loads the disk index if present, then refreshes from the CDN when absent or stale.</summary>
    Task WarmAsync(CancellationToken cancellationToken = default);
}
