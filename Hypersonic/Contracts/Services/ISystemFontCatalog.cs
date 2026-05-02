namespace Hypersonic.Contracts.Services;

/// <summary>
/// Provides a cached, case-insensitive view over the fonts installed on the
/// current system. Used by the font customization UI as the autosuggest source
/// for the font pickers and to validate user-typed font names.
/// </summary>
public interface ISystemFontCatalog
{
    /// <summary>
    /// All installed font family names, sorted by ordinal (case-insensitive).
    /// Computed once on first access and cached for the lifetime of the service.
    /// </summary>
    IReadOnlyList<string> FamilyNames { get; }

    /// <summary>
    /// Returns true when the given family name matches an installed font
    /// (case-insensitive, ordinal compare). Whitespace-only or empty input
    /// returns false.
    /// </summary>
    bool Contains(string? familyName);

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> family names that begin with
    /// <paramref name="query"/> (case-insensitive). When the query is empty or
    /// whitespace, returns the first <paramref name="maxResults"/> families.
    /// </summary>
    IEnumerable<string> Suggest(string? query, int maxResults = 12);
}
