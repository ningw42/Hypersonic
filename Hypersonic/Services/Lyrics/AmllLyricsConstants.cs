namespace Hypersonic.Services.Lyrics;

/// <summary>Constants for the amll-ttml-db source: its provider id, CDN mirror layout, and cache location.</summary>
internal static class AmllLyricsConstants
{
    public const string ProviderId = "amll-ttml-db";
    public const string HttpClientName = "amll";
    public const string CdnBase = "https://cdn.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/";
    public const string IndexPath = "metadata/raw-lyrics-index.jsonl";
    public const string PayloadPathPrefix = "raw-lyrics/";
    public const string CacheSubfolder = "AmllIndex";
    public const string IndexFileName = "raw-lyrics-index.jsonl";
    public const string MetaFileName = "index.meta.json";
}
