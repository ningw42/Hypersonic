using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Tests.Fakes;

/// <summary>A registered provider with an id + display name that never actually fetches. For
/// preferences/VM tests that only need the registered set, not real lyrics.</summary>
public sealed class StubLyricsProvider(string id, string displayName) : ILyricsProvider
{
    public string Id => id;
    public string DisplayName => displayName;

    public Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        => throw new NotSupportedException("StubLyricsProvider does not fetch.");
}
