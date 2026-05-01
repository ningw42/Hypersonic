using System.Globalization;

namespace Hypersonic.Helpers;

/// <summary>
/// Formats the codec/quality "technical line" for the Now Playing page.
/// Format: <c>Codec · BitDepth/SampleRate · Bitrate</c> using U+00B7 separators.
/// Returns <c>null</c> when the line should be hidden entirely (no codec, or
/// codec alone with no quality info).
/// </summary>
public static class TechnicalDetailFormatter
{
    private const string Separator = " · ";

    public static string? Format(string? suffix, int bitDepth, int samplingRate, int bitRate)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var hasComposite = bitDepth > 0 && samplingRate > 0;
        var hasBitRate = bitRate > 0;

        if (!hasComposite && !hasBitRate)
        {
            // Bare codec is noise.
            return null;
        }

        var parts = new List<string>(3) { suffix.ToUpperInvariant() };
        if (hasComposite)
        {
            parts.Add($"{bitDepth}-bit/{FormatSampleRate(samplingRate)}");
        }
        if (hasBitRate)
        {
            parts.Add($"{bitRate} kbps");
        }

        return string.Join(Separator, parts);
    }

    private static string FormatSampleRate(int samplingRate)
    {
        var khz = samplingRate / 1000.0;
        // Drop trailing zeros: 96.0 -> "96", 44.1 -> "44.1".
        var formatted = khz.ToString("0.###", CultureInfo.InvariantCulture);
        return formatted + "kHz";
    }
}
