namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// A single provider's best answer for a <see cref="LyricsQuery"/>. One of four shapes:
/// <list type="bullet">
///   <item><see cref="LyricsOutcomeKind.Content"/> — carries the validated
///     <see cref="Document"/>, the byte-for-byte <see cref="RawPayload"/>, its
///     <see cref="Format"/>, and the <see cref="Definitive"/> bit.</item>
///   <item><see cref="LyricsOutcomeKind.DefinitiveEmpty"/> / <see cref="LyricsOutcomeKind.DefinitiveUnsupported"/>
///     / <see cref="LyricsOutcomeKind.Transient"/> — no document or payload.</item>
/// </list>
/// </summary>
public sealed class LyricsProviderResult
{
    private LyricsProviderResult(
        LyricsOutcomeKind kind,
        LyricsDocument? document,
        ReadOnlyMemory<byte> rawPayload,
        LyricsFormat format,
        bool definitive)
    {
        Kind = kind;
        Document = document;
        RawPayload = rawPayload;
        Format = format;
        Definitive = definitive;
    }

    public LyricsOutcomeKind Kind { get; }

    /// <summary>The validated document — non-null only for <see cref="LyricsOutcomeKind.Content"/>.</summary>
    public LyricsDocument? Document { get; }

    /// <summary>The raw provider payload to persist — only meaningful for <see cref="LyricsOutcomeKind.Content"/>.</summary>
    public ReadOnlyMemory<byte> RawPayload { get; }

    /// <summary>Wire format of the payload — only meaningful for <see cref="LyricsOutcomeKind.Content"/>.</summary>
    public LyricsFormat Format { get; }

    /// <summary>
    /// For a <see cref="LyricsOutcomeKind.Content"/> result, whether the answer is safe to
    /// positive-cache (a better answer cannot still appear once a transient clears). The single
    /// <c>false</c> case is a transient on the higher-quality endpoint plus content on the fallback.
    /// For non-content results this mirrors <see cref="IsDefinitive"/>.
    /// </summary>
    public bool Definitive { get; }

    /// <summary>True when this outcome is not transient (it can gate the negative cache write).</summary>
    public bool IsDefinitive => Kind != LyricsOutcomeKind.Transient;

    public static LyricsProviderResult Content(
        LyricsDocument document, ReadOnlyMemory<byte> rawPayload, LyricsFormat format, bool definitive)
        => new(LyricsOutcomeKind.Content, document, rawPayload, format, definitive);

    public static readonly LyricsProviderResult DefinitiveEmpty =
        new(LyricsOutcomeKind.DefinitiveEmpty, null, default, LyricsFormat.Unknown, definitive: true);

    public static readonly LyricsProviderResult DefinitiveUnsupported =
        new(LyricsOutcomeKind.DefinitiveUnsupported, null, default, LyricsFormat.Unknown, definitive: true);

    public static readonly LyricsProviderResult Transient =
        new(LyricsOutcomeKind.Transient, null, default, LyricsFormat.Unknown, definitive: false);
}
