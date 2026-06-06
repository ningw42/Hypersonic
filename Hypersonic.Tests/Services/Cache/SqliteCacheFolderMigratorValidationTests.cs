using Hypersonic.Services.Cache;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Cache;

/// <summary>
/// Validation tests for the folder migrator: subclasses declare leaf names only, and
/// the migrator enforces a well-formed step list. All violations throw
/// <see cref="ArgumentException"/> — leaf-name checks for <c>PayloadSubfolderName</c>
/// fire lazily on first path resolution; the migrator's own checks fire at its entry
/// point on every init.
/// </summary>
[TestClass]
public class SqliteCacheFolderMigratorValidationTests
{
    private string _parent = null!;

    [TestInitialize]
    public void Setup()
    {
        _parent = Path.Combine(Path.GetTempPath(), "hypersonic-folder-mig-val-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_parent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_parent))
        {
            return;
        }

        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort.
        }
    }

    // ---- Leaf-name validation. ----

    [TestMethod]
    [DataRow("")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("..\\Images")]
    [DataRow("foo\\bar")]
    [DataRow("foo/bar")]
    [DataRow("C:\\foo")]
    [DataRow("\\foo")]
    [DataRow("a<b")]
    public async Task BadPayloadSubfolderName_ThrowsOnFirstCacheCall(string badName)
    {
        // Empty step list ⇒ the throw comes from the base's lazy path resolution.
        using var store = CreateStore(badName, []);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public void BadPayloadSubfolderName_ThrowsOnCacheFolderPathAccess()
    {
        using var store = CreateStore("foo/bar", []);

        Assert.ThrowsExactly<ArgumentException>(() => _ = store.CacheFolderPath);
    }

    [TestMethod]
    public void BadPayloadSubfolderName_DoesNotThrowAtConstruction()
    {
        // Construction must not read the virtual; validation is lazy.
        using var store = CreateStore("..", []);
        Assert.IsNotNull(store);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("foo/bar")]
    [DataRow("C:\\foo")]
    [DataRow("a<b")]
    public async Task BadSourceSubfolder_ThrowsOnFirstCacheCall(string badSource)
    {
        using var store = CreateStore("Images", [Step(1, badSource, "Images")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(".")]
    [DataRow("foo/bar")]
    [DataRow("a<b")]
    public async Task BadMetadataFileName_ThrowsOnFirstCacheCall(string badMetadata)
    {
        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")], metadataFileName: badMetadata);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    // ---- Step-list validation. ----

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task NonPositiveTargetVersion_Throws(int version)
    {
        using var store = CreateStore("Images", [Step(version, "ImageCache", "Images")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public async Task DuplicateTargetVersion_Throws()
    {
        using var store = CreateStore("Images", [Step(1, "A", "B"), Step(1, "B", "Images")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public async Task HighestStepTargetNotEqualToSubfolder_Throws()
    {
        using var store = CreateStore("Images", [Step(1, "ImageCache", "Wrong")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public async Task ChainDiscontinuity_Throws()
    {
        // step 2's source ("X") does not equal step 1's target ("B").
        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "X", "Current")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public async Task NonFinalStepTargetingSubfolder_Throws()
    {
        // step 1 (non-highest) targets the current read folder ("Current") — final-target uniqueness.
        using var store = CreateStore("Current", [Step(1, "A", "Current"), Step(2, "Current", "Current")]);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.GetStatsAsync());
    }

    [TestMethod]
    public async Task SingleStepList_PassesValidation()
    {
        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);

        // Valid declaration ⇒ no throw; fresh install bumps the marker.
        var stats = await store.GetStatsAsync();
        Assert.AreEqual(0, stats.EntryCount);
    }

    // ---- Helpers. ----

    private TestCacheStore CreateStore(
        string subfolder,
        IReadOnlyList<PayloadFolderMigrationStep> steps,
        string? metadataFileName = null)
    {
        return new TestCacheStore(
            Options.Create(new TestCacheOptions()),
            new FixedAppFolderLocator(_parent),
            new RecordingLoggingService())
        {
            SubfolderName = subfolder,
            MigrationStepsValue = steps,
            MetadataFileNameValue = metadataFileName,
        };
    }

    private static PayloadFolderMigrationStep Step(int version, string source, string target)
        => new(version, source, target);
}
