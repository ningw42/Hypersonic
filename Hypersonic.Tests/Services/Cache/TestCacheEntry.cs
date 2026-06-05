using Hypersonic.Services.Cache;

namespace Hypersonic.Tests.Services.Cache;

internal sealed class TestCacheEntry : ICacheEntry
{
    public string FileName { get; set; } = string.Empty;
    public long Length { get; set; }
    public DateTimeOffset LastAccessUtc { get; set; }
}
