using System.Text.Json;

using Hypersonic.Client.Models.Entities;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Parses the cached <c>subsonic-structured</c> payload (a single re-serialized
/// <see cref="StructuredLyrics"/> body) into a <see cref="LyricsDocument"/>.
///
/// <para>Line-sync is all-or-nothing: <c>Synced==true</c> <b>and</b> every line carrying a valid
/// non-negative, monotonic <c>Start</c> yields a <see cref="LyricsSyncKind.Line"/> document;
/// otherwise the whole document downgrades to <see cref="LyricsSyncKind.Plain"/> (non-blank values,
/// <c>StartMs=0</c>). Subsonic structured never carries word timing, so <see cref="LyricsSyncKind.Word"/>
/// is never produced.</para>
/// </summary>
internal sealed class SubsonicStructuredLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public SubsonicStructuredLyricsParser(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(SubsonicStructuredLyricsParser));
    }

    public LyricsFormat Format => LyricsFormat.SubsonicStructured;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        StructuredLyrics? entry;
        try
        {
            entry = JsonSerializer.Deserialize<StructuredLyrics>(payload.Span, Json.CompactOptions);
        }
        catch (JsonException ex)
        {
            _log.Warning("Structured lyrics payload failed to deserialize; discarding", exception: ex);
            return null;
        }

        var lines = entry?.Line;
        if (lines is null || lines.Length == 0)
        {
            return null;
        }

        LyricsDocument document;
        if (entry!.Synced && TryBuildSyncedLines(lines, out var syncedLines))
        {
            document = new LyricsDocument
            {
                SyncKind = LyricsSyncKind.Line,
                Format = LyricsFormat.SubsonicStructured,
                Lines = syncedLines,
                LanguageCode = entry.Lang,
                OffsetMs = ToInt(entry.Offset),
            };
        }
        else
        {
            var plainLines = BuildPlainLines(lines);
            if (plainLines.Count == 0)
            {
                return null;
            }

            document = new LyricsDocument
            {
                SyncKind = LyricsSyncKind.Plain,
                Format = LyricsFormat.SubsonicStructured,
                Lines = plainLines,
                LanguageCode = entry.Lang,
                OffsetMs = ToInt(entry.Offset),
            };
        }

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning(
                "Structured lyrics document failed validation; discarding",
                new { entry.Synced, IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }

    private static bool TryBuildSyncedLines(LyricLine?[] lines, out List<LyricsLine> built)
    {
        built = new List<LyricsLine>(lines.Length);
        long previousStart = long.MinValue;

        foreach (var line in lines)
        {
            if (line?.Start is not { } start || start < 0 || start > int.MaxValue || start < previousStart)
            {
                built = [];
                return false;
            }

            built.Add(new LyricsLine { StartMs = (int)start, Text = line.Value ?? string.Empty });
            previousStart = start;
        }

        return built.Count > 0;
    }

    private static List<LyricsLine> BuildPlainLines(LyricLine?[] lines)
    {
        var result = new List<LyricsLine>(lines.Length);
        foreach (var line in lines)
        {
            if (line is { Value: { } value } && !string.IsNullOrWhiteSpace(value))
            {
                result.Add(new LyricsLine { StartMs = 0, Text = value });
            }
        }

        return result;
    }

    private static int? ToInt(long? value)
        => value is { } v && v >= int.MinValue && v <= int.MaxValue ? (int)v : null;
}
