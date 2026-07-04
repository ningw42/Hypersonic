namespace Hypersonic.Contracts.Services;

/// <summary>Resolves a lyrics provider id (the value in <c>LyricsDocument.SourceProvider</c>) to its
/// human display name. Built once from the registered providers.</summary>
public interface ILyricsSourceCatalog
{
    /// <summary>Display name for <paramref name="providerId"/>, or <c>null</c> when the id is unknown or null.</summary>
    string? DisplayNameFor(string? providerId);
}
