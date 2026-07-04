using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;

namespace Hypersonic.Services;

/// <summary>Static id→DisplayName map built from the registered <see cref="ILyricsProvider"/> set.</summary>
internal sealed class LyricsSourceCatalog : ILyricsSourceCatalog
{
    private readonly Dictionary<string, string> _names;

    public LyricsSourceCatalog(IEnumerable<ILyricsProvider> providers)
        => _names = providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);

    public string? DisplayNameFor(string? providerId)
        => providerId is not null && _names.TryGetValue(providerId, out var name) ? name : null;
}
