using System.Text.Json;

using Hypersonic.Core.Helpers;
using Hypersonic.Services.Cache;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Cache;

/// <summary>
/// Tests for the declarative payload-folder migrator, driven through a
/// <see cref="TestCacheStore"/> whose subfolder name and migration steps are
/// configurable. Cases mirror the design's state-machine matrix.
/// </summary>
[TestClass]
public class SqliteCacheFolderMigratorTests
{
    private string _parent = null!;
    private RecordingLoggingService _logging = null!;

    [TestInitialize]
    public void Setup()
    {
        _parent = Path.Combine(Path.GetTempPath(), "hypersonic-folder-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_parent);
        _logging = new RecordingLoggingService();
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

    // ---- Single-step (image-cache-shaped) scenarios. ----

    [TestMethod]
    public async Task FreshInstall_SourceAndTargetAbsent_BumpsMarkerWithoutMoving()
    {
        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);

        await store.GetStatsAsync();

        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "ImageCache")), "No source folder should be created.");
        Assert.IsTrue(Directory.Exists(Path.Combine(_parent, "Images")), "Init creates the payload folder.");
    }

    [TestMethod]
    public async Task ExistingSource_TargetAbsent_MovesDirectoryAndBumpsMarker()
    {
        SeedFolderWithFile("ImageCache", "data.bin", [1, 2, 3]);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "ImageCache")), "Source folder must be gone after move.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Payload must land at the target folder.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task SourceAbsent_TargetPresent_MarkerAbsent_BumpsMarkerWithoutMoving()
    {
        // "Already migrated externally" (e.g. unpackaged install that always used "Images").
        SeedFolderWithFile("Images", "existing.bin", [9]);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "existing.bin")), "Existing target data must be untouched.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task FinalTargetConflict_WarnsAndContinuesWithoutBumping()
    {
        SeedFolderWithFile("ImageCache", "old.bin", [1]);
        SeedFolderWithFile("Images", "new.bin", [2]);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        // Conflict on the final target: neither folder touched, no marker bump, init continues.
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "ImageCache", "old.bin")), "Source must be left intact.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "new.bin")), "Existing target must be left intact.");
        Assert.IsNull(await ReadMarkerVersionAsync("Images.json"), "Marker must not be bumped on a final-target conflict.");
        Assert.IsTrue(_logging.Warnings.Count >= 1, "A conflict must be logged.");
        // Init continues: the store opens the existing target and a DB is created there.
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "cache.db")), "Init must continue and open the existing target.");
    }

    [TestMethod]
    public async Task DirectoryMoveFailure_FailsInitWithoutCreatingTarget_ThenRecovers()
    {
        SeedFolderWithFile("ImageCache", "data.bin", [1, 2, 3]);
        // A file occupying the target path makes Directory.Move throw deterministically.
        await File.WriteAllBytesAsync(Path.Combine(_parent, "Images"), [0]);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount, "A failed migration leaves the cache unavailable (safe default).");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "ImageCache", "data.bin")), "Source must remain intact on a move failure.");
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "Images")), "Target folder must not be created on a move failure.");
        Assert.IsNull(await ReadMarkerVersionAsync("Images.json"), "Marker must not be bumped on a move failure.");

        // Resolve the obstacle and re-run init: the same store retries and recovers in-session.
        File.Delete(Path.Combine(_parent, "Images"));
        await store.GetStatsAsync();

        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "ImageCache")), "Recovery move clears the source.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Recovery move lands the payload.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task IdentityStep_NoMove_BumpsMarker()
    {
        SeedFolderWithFile("Images", "x.bin", [7]);

        using var store = CreateStore("Images", [Step(1, "Images", "Images")]);
        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "x.bin")), "Identity step must not disturb data.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task MalformedMarker_TreatedAsVersionZero_AndReEvaluatesCleanly()
    {
        await File.WriteAllTextAsync(Path.Combine(_parent, "Images.json"), "this is not json {");

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        // v0 baseline ⇒ fresh-install no-op ⇒ marker rewritten as a valid v1 document.
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task MarkerWriteFailure_DoesNotStrandData_AndRecoversNextRun()
    {
        SeedFolderWithFile("ImageCache", "data.bin", [1, 2, 3]);
        // A directory occupying the marker path makes the marker write fail; the move still happens.
        Directory.CreateDirectory(Path.Combine(_parent, "Images.json"));

        using (var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]))
        {
            await store.GetStatsAsync();
        }
        SqliteConnection.ClearAllPools();

        // Move completed despite the failed marker write — data is not stranded.
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "ImageCache")), "Move must complete even if the marker write fails.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Payload must be at the target.");
        Assert.IsTrue(Directory.Exists(Path.Combine(_parent, "Images.json")), "Marker write failed (path is a directory).");

        // Next run with the marker path writable recovers via "already migrated externally" — no re-move.
        Directory.Delete(Path.Combine(_parent, "Images.json"));
        using var store2 = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store2.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Data must not be re-moved or lost on recovery.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"));
    }

    [TestMethod]
    public async Task CacheParentDoesNotExist_MigratorCreatesIt()
    {
        var nestedParent = Path.Combine(_parent, "nested", "cache");
        Assert.IsFalse(Directory.Exists(nestedParent));

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")], cacheParent: nestedParent);
        await store.GetStatsAsync();

        Assert.IsTrue(Directory.Exists(nestedParent), "Migrator must bootstrap the cache parent.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json", nestedParent));
    }

    // ---- Multi-step chain scenarios (A -> B -> Current). ----

    [TestMethod]
    public async Task MultiStepChain_AdvancesMarkerThroughEachStep()
    {
        SeedFolderWithFile("A", "a.bin", [1]);

        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "B", "Current")]);
        await store.GetStatsAsync();

        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "A")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "B")));
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Current", "a.bin")), "Data must be carried through the whole chain.");
        Assert.AreEqual(2, await ReadMarkerVersionAsync("Current.json"));
    }

    [TestMethod]
    public async Task MoveFailureMidChain_LeavesMarkerAtLastSuccessfulStep()
    {
        SeedFolderWithFile("A", "a.bin", [1]);
        // A file at the final target makes step 2's move throw, after step 1 succeeded.
        await File.WriteAllBytesAsync(Path.Combine(_parent, "Current"), [0]);

        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "B", "Current")]);
        await store.GetStatsAsync();

        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "A")), "Step 1's move completed.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "B", "a.bin")), "Data sits at the last successfully-migrated folder.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Current.json"), "Marker reflects the highest completed step.");
    }

    [TestMethod]
    public async Task PreFinalTargetConflict_FailsInitWithoutCreatingFinalTarget_ThenRecovers()
    {
        // Chain A -> B -> Current, with both A and B populated and Current absent.
        SeedFolderWithFile("A", "a.bin", [1]);
        SeedFolderWithFile("B", "b.bin", [2]);

        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "B", "Current")]);
        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount, "A pre-final conflict fails init (safe default).");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "A", "a.bin")), "Source must remain intact.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "B", "b.bin")), "Conflicted intermediate must remain intact.");
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "Current")), "Final target must not be created.");
        Assert.IsNull(await ReadMarkerVersionAsync("Current.json"), "No marker bump on a pre-final conflict.");
        Assert.IsTrue(_logging.Warnings.Count >= 1, "A pre-final conflict must be logged.");

        // Resolve by removing the conflicted intermediate, then recover in-session.
        Directory.Delete(Path.Combine(_parent, "B"), recursive: true);
        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Current", "a.bin")), "Recovery carries A's data to the final folder.");
        Assert.AreEqual(2, await ReadMarkerVersionAsync("Current.json"));
    }

    [TestMethod]
    public async Task Preflight_DownstreamTargetExistsPopulated_FailsInit_ThenRecovers()
    {
        // Chain A -> B -> Current: A populated, B absent, Current populated.
        SeedFolderWithFile("A", "a.bin", [1]);
        SeedFolderWithFile("Current", "c.bin", [2]);

        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "B", "Current")]);
        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount, "Preflight aborts init before any move.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "A", "a.bin")), "Source must remain intact (no move happened).");
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "B")), "Intermediate must not be created by an aborted move.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Current", "c.bin")), "Pre-existing downstream must remain intact.");
        Assert.IsNull(await ReadMarkerVersionAsync("Current.json"), "No marker bump when preflight aborts.");
        Assert.IsTrue(_logging.Warnings.Count >= 1, "An ambiguous chain state must be logged.");

        // Remove the stale source and re-run: A -> B no-ops, B -> Current is "already migrated externally".
        Directory.Delete(Path.Combine(_parent, "A"), recursive: true);
        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Current", "c.bin")), "Existing downstream data must be preserved.");
        Assert.AreEqual(2, await ReadMarkerVersionAsync("Current.json"));
    }

    [TestMethod]
    public async Task Preflight_DownstreamTargetExistsEmpty_FailsInit()
    {
        // Regression guard: preflight uses Directory.Exists, not "is populated". An empty
        // downstream folder must still abort step 1's move.
        SeedFolderWithFile("A", "a.bin", [1]);
        Directory.CreateDirectory(Path.Combine(_parent, "Current")); // empty

        using var store = CreateStore("Current", [Step(1, "A", "B"), Step(2, "B", "Current")]);
        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount);
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "A", "a.bin")), "Source must remain intact.");
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "B")), "No move may happen when an empty downstream exists.");
        Assert.IsFalse(File.Exists(Path.Combine(_parent, "Current", "cache.db")), "Init must fail; no DB opened at the final target.");
        Assert.IsNull(await ReadMarkerVersionAsync("Current.json"));
    }

    [TestMethod]
    public async Task CacheParentCannotBeCreated_FailsInitGracefullyWithoutThrowing()
    {
        // A file occupying the cache-parent path makes Directory.CreateDirectory(parent) throw.
        // That must be handled under the migrator's failure contract, not bubble out of a cache
        // read (callers only catch SqliteException).
        var blockedParent = Path.Combine(_parent, "blocked");
        await File.WriteAllBytesAsync(blockedParent, [0]);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")], cacheParent: blockedParent);

        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount, "A parent-bootstrap failure must degrade to the cache-unavailable safe default.");
        Assert.AreEqual(0L, stats.TotalBytes);
    }

    [TestMethod]
    public async Task MarkerAheadOfDeclaredSteps_WithLegacySource_ReEvaluatesAndMigrates()
    {
        // A stale/downgrade marker claims a version higher than any declared step while the
        // legacy source still exists and the target is absent. Trusting the marker would skip
        // the move and then open an empty target, stranding the legacy cache. The migrator must
        // treat an ahead marker as untrustworthy and re-evaluate the state table.
        SeedFolderWithFile("ImageCache", "data.bin", [1, 2, 3]);
        await WriteMarkerAsync("Images.json", version: 2);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "ImageCache")), "Legacy source must be migrated, not stranded.");
        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Payload must land at the target.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"), "Marker is rewritten down to the highest declared version.");
    }

    [TestMethod]
    public async Task MarkerAheadOfDeclaredSteps_AlreadyMigrated_PreservesDataAndClampsMarker()
    {
        // Benign downgrade: a newer version migrated further (marker ahead), the data already
        // lives at the target, and the source is gone. Re-evaluating must keep the data and
        // bring the marker back in line with the declared steps.
        SeedFolderWithFile("Images", "data.bin", [9]);
        await WriteMarkerAsync("Images.json", version: 5);

        using var store = CreateStore("Images", [Step(1, "ImageCache", "Images")]);
        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_parent, "Images", "data.bin")), "Existing data must be preserved.");
        Assert.AreEqual(1, await ReadMarkerVersionAsync("Images.json"), "Marker clamps to the highest declared version.");
    }

    [TestMethod]
    public async Task Migrator_HonorsCancellationBeforeMoving()
    {
        SeedFolderWithFile("ImageCache", "data.bin", [1, 2, 3]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await SqliteCacheFolderMigrator.EnsurePayloadFolderAsync(
                new FixedAppFolderLocator(_parent),
                "Images",
                "Images.json",
                [Step(1, "ImageCache", "Images")],
                _logging.GetLogger("test"),
                cts.Token));

        Assert.IsTrue(Directory.Exists(Path.Combine(_parent, "ImageCache")), "Cancellation must abort before moving the source.");
        Assert.IsFalse(Directory.Exists(Path.Combine(_parent, "Images")), "No target may be created on a cancelled migration.");
        Assert.IsNull(await ReadMarkerVersionAsync("Images.json"), "No marker may be written on a cancelled migration.");
    }

    [TestMethod]
    public async Task GreenfieldStore_EmptySteps_WritesNoMarker()
    {
        using var store = CreateStore("Lyrics", []);
        await store.GetStatsAsync();

        Assert.IsTrue(Directory.Exists(Path.Combine(_parent, "Lyrics")), "Init still creates the payload folder.");
        Assert.IsFalse(File.Exists(Path.Combine(_parent, "Lyrics.json")), "Greenfield stores write no marker file.");
    }

    // ---- Helpers. ----

    private TestCacheStore CreateStore(
        string subfolder,
        IReadOnlyList<PayloadFolderMigrationStep> steps,
        string? cacheParent = null,
        string? metadataFileName = null)
    {
        return new TestCacheStore(
            Options.Create(new TestCacheOptions()),
            new FixedAppFolderLocator(cacheParent ?? _parent),
            _logging)
        {
            SubfolderName = subfolder,
            MigrationStepsValue = steps,
            MetadataFileNameValue = metadataFileName,
        };
    }

    private static PayloadFolderMigrationStep Step(int version, string source, string target)
        => new(version, source, target);

    private void SeedFolderWithFile(string subfolder, string fileName, byte[] content)
    {
        var dir = Path.Combine(_parent, subfolder);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, fileName), content);
    }

    private Task<int?> ReadMarkerVersionAsync(string fileName) => ReadMarkerVersionAsync(fileName, _parent);

    private static async Task<int?> ReadMarkerVersionAsync(string fileName, string parent)
    {
        var path = Path.Combine(parent, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path);
        var meta = JsonSerializer.Deserialize<PayloadFolderMetadata>(text, Json.Options);
        return meta?.FolderVersion;
    }

    private async Task WriteMarkerAsync(string fileName, int version)
    {
        var json = JsonSerializer.Serialize(new PayloadFolderMetadata(version), Json.Options);
        await File.WriteAllTextAsync(Path.Combine(_parent, fileName), json);
    }
}
