namespace Hypersonic.Models;

/// <summary>
/// Options for the amll-ttml-db provider. <see cref="Enabled"/> is the seam for the deferred
/// legal-posture settings toggle; it defaults to <c>true</c> (always-on for now).
/// </summary>
public class AmllLyricsOptions
{
    /// <summary>Disk-index staleness threshold that triggers a conditional refresh.</summary>
    public int RefreshIntervalDays { get; set; } = 7;

    /// <summary>When false, the provider is inert and the index does no network/disk work.</summary>
    public bool Enabled { get; set; } = true;
}
