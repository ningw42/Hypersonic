using Hypersonic.Services.Cache;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Services.Cache;

[TestClass]
public class SubsonicCacheScopeTests
{
    private static FakeSubsonicClientService Svc(string? identity)
        => new() { Client = new RecordingSubsonicClient(), ActiveUserIdentity = identity };

    [TestMethod]
    public void TryBuildSongKeyHash_NoClient_ReturnsFalse()
    {
        var svc = new FakeSubsonicClientService { Client = null, ActiveUserIdentity = "alice" };
        Assert.IsFalse(SubsonicCacheScope.TryBuildSongKeyHash(svc, "song-1", out _));
    }

    [TestMethod]
    public void TryBuildSongKeyHash_EmptyUserIdentity_ReturnsFalse()
    {
        Assert.IsFalse(SubsonicCacheScope.TryBuildSongKeyHash(Svc(null), "song-1", out _));
        Assert.IsFalse(SubsonicCacheScope.TryBuildSongKeyHash(Svc(string.Empty), "song-1", out _));
    }

    [TestMethod]
    public void TryBuildSongKeyHash_BlankSongId_ReturnsFalse()
    {
        Assert.IsFalse(SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "  ", out _));
    }

    [TestMethod]
    public void TryBuildSongKeyHash_Valid_IsStableAndHexLowercase()
    {
        Assert.IsTrue(SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "song-1", out var first));
        Assert.IsTrue(SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "song-1", out var second));

        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first.Length);
        Assert.IsTrue(first.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [TestMethod]
    public void TryBuildSongKeyHash_DifferentUsers_ProduceDifferentHashes()
    {
        SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "song-1", out var alice);
        SubsonicCacheScope.TryBuildSongKeyHash(Svc("bob"), "song-1", out var bob);

        Assert.AreNotEqual(alice, bob);
    }

    [TestMethod]
    public void TryBuildSongKeyHash_DifferentSongs_ProduceDifferentHashes()
    {
        SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "song-1", out var one);
        SubsonicCacheScope.TryBuildSongKeyHash(Svc("alice"), "song-2", out var two);

        Assert.AreNotEqual(one, two);
    }
}
