using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Contracts.Lyrics;

/// <summary>
/// One real lyrics source, self-contained. A provider owns its internal endpoint fallback and
/// format selection and returns its single best <see cref="LyricsProviderResult"/>. The unit of
/// a provider is a <em>source</em>, never a format or an endpoint. Stateless.
/// </summary>
public interface ILyricsProvider
{
    /// <summary>Stable identifier for this source (also the cached <c>SourceProvider</c>), e.g. <c>"subsonic"</c>.</summary>
    string Id { get; }

    /// <summary>Resolves the source's best result for <paramref name="query"/>.</summary>
    Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken);
}
