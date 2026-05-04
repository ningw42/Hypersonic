namespace Hypersonic.Core.Models;

public sealed record ImageRequestOptions(string? Size = null)
{
    public static ImageRequestOptions Default { get; } = new();

    public static ImageRequestOptions ForSize(string? size)
    {
        return string.IsNullOrWhiteSpace(size)
            ? Default
            : new ImageRequestOptions(size.Trim());
    }

    public string NormalizedSize => string.IsNullOrWhiteSpace(Size) ? string.Empty : Size.Trim();
}
