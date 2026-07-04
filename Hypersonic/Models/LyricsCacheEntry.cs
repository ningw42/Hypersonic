using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services.Cache;

namespace Hypersonic.Models;

/// <summary>
/// Index row for the durable lyrics cache. Beyond the <see cref="ICacheEntry"/> essentials
/// (<see cref="FileName"/> — empty for negative rows; <see cref="Length"/>;
/// <see cref="LastAccessUtc"/>) it carries the metadata the service needs to pick a parser and make
/// TTL decisions without touching the payload file.
/// </summary>
public sealed class LyricsCacheEntry : ICacheEntry
{
    /// <summary>Payload file name; empty for negative ("definitively no lyrics") rows.</summary>
    public string FileName { get; set; } = string.Empty;

    public long Length { get; set; }

    /// <summary>Id of the provider that produced this entry (e.g. <c>"subsonic"</c>).</summary>
    public string SourceProvider { get; set; } = string.Empty;

    /// <summary>Wire format selecting the read-time parser; <see cref="LyricsFormat.Unknown"/> for negative rows.</summary>
    public LyricsFormat Format { get; set; } = LyricsFormat.Unknown;

    public LyricsSyncKind SyncKind { get; set; }

    public string? LanguageCode { get; set; }

    public int? OffsetMs { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastAccessUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public LyricsCacheEntry Clone() => new()
    {
        FileName = FileName,
        Length = Length,
        SourceProvider = SourceProvider,
        Format = Format,
        SyncKind = SyncKind,
        LanguageCode = LanguageCode,
        OffsetMs = OffsetMs,
        CreatedAtUtc = CreatedAtUtc,
        LastAccessUtc = LastAccessUtc,
        ExpiresAtUtc = ExpiresAtUtc,
    };
}
