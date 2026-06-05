using Hypersonic.Services.Cache;

namespace Hypersonic.Models;

public class ImageCacheOptions : CacheOptionsBase
{
    public ImageCacheOptions()
    {
        MaximumBytes = 256L * 1024L * 1024L;
    }

    public int TimeToLiveDays { get; set; } = 30;
}
