using Hypersonic.Helpers;
using Hypersonic.ViewModels;
using Hypersonic.Views;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class LastPagePersistencePolicyTests
{
    [TestMethod]
    public void NowPlayingPage_NotPersisted()
    {
        Assert.IsNull(LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(NowPlayingPage)));
    }

    [TestMethod]
    public void Null_ReturnsNull()
    {
        Assert.IsNull(LastPagePersistencePolicy.GetPersistableViewModelKey(null));
    }

    [TestMethod]
    public void DetailPages_NotPersisted()
    {
        Assert.IsNull(LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(AlbumDetailPage)));
        Assert.IsNull(LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(ArtistDetailPage)));
        Assert.IsNull(LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(PlaylistDetailPage)));
    }

    [TestMethod]
    public void TopLevelPages_ReturnVmKey()
    {
        Assert.AreEqual(typeof(MainViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(MainPage)));
        Assert.AreEqual(typeof(AlbumsViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(AlbumsPage)));
        Assert.AreEqual(typeof(ArtistsViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(ArtistsPage)));
        Assert.AreEqual(typeof(TracksViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(TracksPage)));
        Assert.AreEqual(typeof(PlaylistsViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(PlaylistsPage)));
        Assert.AreEqual(typeof(SettingsViewModel).FullName,
            LastPagePersistencePolicy.GetPersistableViewModelKey(typeof(SettingsPage)));
    }
}
