using Hypersonic.Services.Cache;

namespace Hypersonic.Models;

/// <summary>
/// Options for the durable lyrics cache. Lyrics payloads are tiny, so the byte cap is modest.
/// TTL is asymmetric: positive entries live long, negative ("definitively no lyrics") entries
/// expire sooner so a track that later gains lyrics gets re-checked.
/// </summary>
public class LyricsCacheOptions : CacheOptionsBase
{
    public LyricsCacheOptions()
    {
        MaximumBytes = 64L * 1024L * 1024L;
    }

    public int PositiveTimeToLiveDays { get; set; } = 30;

    public int NegativeTimeToLiveDays { get; set; } = 7;
}
