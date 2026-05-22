using System.Text.Json.Serialization;

namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// One timed line of lyrics. May carry word-level timing in <see cref="Words"/>;
/// an empty list means line-only sync.
/// </summary>
public class LyricsLine
{
    private readonly IReadOnlyList<LyricsWord> _words = Array.Empty<LyricsWord>();

    public int StartMs { get; init; }
    public int? EndMs { get; init; }
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<LyricsWord> Words
    {
        get => _words;
        init => _words = value is null
            ? Array.Empty<LyricsWord>()
            : value.ToList().AsReadOnly();
    }

    [JsonIgnore]
    public int DurationMs => EndMs.HasValue ? Math.Max(EndMs.Value - StartMs, 0) : 0;
}
