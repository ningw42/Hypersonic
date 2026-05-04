namespace Hypersonic.Models;

public class ImageCacheOptions
{
    public string? RootPath { get; set; }

    public long MaximumBytes { get; set; } = 256L * 1024L * 1024L;

    public int TimeToLiveDays { get; set; } = 30;
}
