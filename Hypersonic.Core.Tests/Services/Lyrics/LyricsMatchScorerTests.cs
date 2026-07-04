using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LyricsMatchScorerTests
{
    private static LyricsQuery Query(string title = "Blinding Lights", string? artist = "The Weeknd",
        string? album = "After Hours", int? dur = 200)
        => new("song-1", Artist: artist, Title: title, Album: album, DurationSeconds: dur);

    [TestMethod]
    public void ExactMatch_ScoresHigh()
    {
        var score = LyricsMatchScorer.Score(Query(),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"expected near-perfect, got {score}");
    }

    [TestMethod]
    public void WrongSong_ScoresLow()
    {
        var score = LyricsMatchScorer.Score(Query(),
            new LyricsCandidate("Bohemian Rhapsody", "Queen", "A Night at the Opera", 355));
        Assert.IsTrue(score <= 25, $"expected low, got {score}");
    }

    [TestMethod]
    public void ExtraCandidateArtist_IsIgnored()
    {
        // Query artist is a subset of the candidate's — should not be penalized.
        var score = LyricsMatchScorer.Score(Query(artist: "The Weeknd"),
            new LyricsCandidate("Blinding Lights", "The Weeknd/ROSALÍA", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"extra artist should not hurt, got {score}");
    }

    [TestMethod]
    public void MissingQueriedArtist_IsPenalized()
    {
        // Query has two artists; candidate only one. best(A)=1, best(B)~0 → artist factor ~0.5.
        var full = LyricsMatchScorer.Score(Query(artist: "A/B"),
            new LyricsCandidate("Blinding Lights", "A/B", "After Hours", 200));
        var half = LyricsMatchScorer.Score(Query(artist: "A/B"),
            new LyricsCandidate("Blinding Lights", "A", "After Hours", 200));
        Assert.IsTrue(half < full, $"missing collaborator should lower score ({half} !< {full})");
    }

    [TestMethod]
    public void MissingDuration_IsNeutral_NotPenalized()
    {
        var withDur = LyricsMatchScorer.Score(Query(dur: 200),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 260)); // 60s off
        var noDur = LyricsMatchScorer.Score(Query(dur: null),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", null));
        Assert.IsTrue(noDur > withDur, "absent duration must not penalize like a mismatch does");
    }

    [TestMethod]
    public void CaseAndWidth_AreNormalized()
    {
        var score = LyricsMatchScorer.Score(Query(title: "blinding lights"),
            new LyricsCandidate("ＢＬＩＮＤＩＮＧ　ＬＩＧＨＴＳ", "The Weeknd", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"full-width + case should normalize, got {score}");
    }

    [TestMethod]
    public void QualifierSuffix_IsStripped()
    {
        // A trailing parenthetical/bracket qualifier on the candidate title must not lower the score.
        var score = LyricsMatchScorer.Score(Query(title: "Blinding Lights"),
            new LyricsCandidate("Blinding Lights (Remastered 2021)", "The Weeknd", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"trailing qualifier should be stripped, got {score}");
    }

    [TestMethod]
    public void ArtistConnector_AmpersandAndWord_TokenizeIdentically()
    {
        // "A & B", "A and B", and "A/B" must all tokenize to [a, b], so an "&" query matches an "and" candidate.
        var amp   = LyricsMatchScorer.Score(Query(artist: "A & B"), new LyricsCandidate("Blinding Lights", "A & B",   "After Hours", 200));
        var word  = LyricsMatchScorer.Score(Query(artist: "A & B"), new LyricsCandidate("Blinding Lights", "A and B", "After Hours", 200));
        var slash = LyricsMatchScorer.Score(Query(artist: "A & B"), new LyricsCandidate("Blinding Lights", "A/B",     "After Hours", 200));
        Assert.AreEqual(amp, word, "'and' must canonicalize to the same tokens as '&'");
        Assert.AreEqual(amp, slash, "'/' must canonicalize to the same tokens as '&'");
        Assert.IsTrue(word >= 95, $"'&' query vs 'and' candidate should score high, got {word}");
    }
}
