using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Quality ordering of lyric sync granularity: <c>Word &gt; Line &gt; Plain &gt; None</c>. Used to pick
/// the best entry within a source (the provider) and the best result across sources (the policy), so
/// the two never disagree on what "better" means.
/// </summary>
internal static class LyricsSyncKindRank
{
    public static int Of(LyricsSyncKind kind) => kind switch
    {
        LyricsSyncKind.Word => 3,
        LyricsSyncKind.Line => 2,
        LyricsSyncKind.Plain => 1,
        _ => 0,
    };
}
