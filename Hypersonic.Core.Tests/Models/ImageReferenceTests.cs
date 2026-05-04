using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Models;

[TestClass]
public class ImageReferenceTests
{
    [TestMethod]
    public void SubsonicCoverArt_UsesProviderNeutralShape()
    {
        var reference = ImageReference.SubsonicCoverArt(" cover-id ", "v1");

        Assert.IsNotNull(reference);
        Assert.AreEqual(ImageReference.SubsonicProvider, reference.Provider);
        Assert.AreEqual(ImageReference.CoverArtKind, reference.Kind);
        Assert.AreEqual("cover-id", reference.Identity);
        Assert.AreEqual("v1", reference.Version);
        Assert.IsNull(reference.RemoteUrl);
    }

    [TestMethod]
    public void SubsonicCoverArt_EmptyIdentifierReturnsNull()
    {
        Assert.IsNull(ImageReference.SubsonicCoverArt(" "));
    }

    [TestMethod]
    public void Remote_HashesCredentialAndQuerySensitiveIdentity()
    {
        var reference = ImageReference.Remote("https://alice:secret@example.test/image.jpg?token=abc123");

        Assert.IsNotNull(reference);
        Assert.AreEqual(ImageReference.RemoteProvider, reference.Provider);
        Assert.AreEqual(ImageReference.RemoteImageKind, reference.Kind);
        Assert.AreEqual(64, reference.Identity.Length);
        Assert.IsFalse(reference.Identity.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(reference.Identity.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(reference.Identity.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(reference.Identity.Contains("abc123", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(reference.RemoteUrl);
    }

    [TestMethod]
    public void Remote_NonHttpUrlReturnsNull()
    {
        Assert.IsNull(ImageReference.Remote("file:///tmp/image.jpg"));
    }
}
