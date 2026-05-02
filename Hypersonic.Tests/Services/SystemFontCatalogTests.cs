using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services;
using Hypersonic.Services;

namespace Hypersonic.Tests.Services;

[TestClass]
public class SystemFontCatalogTests
{
    private static SystemFontCatalog Create(IEnumerable<string> fonts)
    {
        return new SystemFontCatalog(new LoggingService(), () => fonts);
    }

    // --- FamilyNames: dedup, sort, trim, drop blanks ---

    [TestMethod]
    public void FamilyNames_SortedOrdinalIgnoreCase()
    {
        var catalog = Create(["Verdana", "Arial", "Calibri"]);

        CollectionAssert.AreEqual(
            new[] { "Arial", "Calibri", "Verdana" },
            catalog.FamilyNames.ToArray());
    }

    [TestMethod]
    public void FamilyNames_DedupesCaseInsensitively()
    {
        var catalog = Create(["Arial", "ARIAL", "arial", "Calibri"]);

        Assert.AreEqual(2, catalog.FamilyNames.Count);
        Assert.IsTrue(catalog.FamilyNames.Contains("Arial") || catalog.FamilyNames.Contains("ARIAL") || catalog.FamilyNames.Contains("arial"));
        Assert.IsTrue(catalog.FamilyNames.Contains("Calibri"));
    }

    [TestMethod]
    public void FamilyNames_DropsBlankAndTrims()
    {
        var catalog = Create(["", "  ", "  Arial  ", "Calibri"]);

        Assert.AreEqual(2, catalog.FamilyNames.Count);
        CollectionAssert.AreEqual(new[] { "Arial", "Calibri" }, catalog.FamilyNames.ToArray());
    }

    [TestMethod]
    public void FamilyNames_PreservesLongPreferredFamilyNames()
    {
        var name = "Berkeley Mono ExtraCondensed Thin";
        var catalog = Create([name]);

        CollectionAssert.AreEqual(new[] { name }, catalog.FamilyNames.ToArray());
    }

    [TestMethod]
    public void FamilyNames_EmptyEnumeratorYieldsEmptyList()
    {
        var catalog = Create([]);

        Assert.AreEqual(0, catalog.FamilyNames.Count);
    }

    // --- Lazy: enumerator only invoked once even on repeated access ---

    [TestMethod]
    public void Enumerator_InvokedOnceOnDemand()
    {
        var calls = 0;
        var catalog = new SystemFontCatalog(
            new LoggingService(),
            () =>
            {
                calls++;
                return ["Arial", "Calibri"];
            });

        Assert.AreEqual(0, calls); // not yet materialized
        _ = catalog.FamilyNames;
        _ = catalog.FamilyNames;
        _ = catalog.Contains("Arial");
        _ = catalog.Suggest("c");

        Assert.AreEqual(1, calls);
    }

    // --- Contains ---

    [TestMethod]
    public void Contains_CaseInsensitiveMatch()
    {
        var catalog = Create(["Arial", "Calibri", "Microsoft YaHei UI"]);

        Assert.IsTrue(catalog.Contains("Arial"));
        Assert.IsTrue(catalog.Contains("arial"));
        Assert.IsTrue(catalog.Contains("ARIAL"));
        Assert.IsTrue(catalog.Contains("microsoft yahei ui"));
    }

    [TestMethod]
    public void Contains_TrimsBeforeMatch()
    {
        var catalog = Create(["Arial"]);

        Assert.IsTrue(catalog.Contains("  Arial  "));
    }

    [TestMethod]
    public void Contains_NullOrWhitespace_ReturnsFalse()
    {
        var catalog = Create(["Arial"]);

        Assert.IsFalse(catalog.Contains(null));
        Assert.IsFalse(catalog.Contains(""));
        Assert.IsFalse(catalog.Contains("   "));
    }

    [TestMethod]
    public void Contains_UnknownFamily_ReturnsFalse()
    {
        var catalog = Create(["Arial"]);

        Assert.IsFalse(catalog.Contains("Cascadia Code"));
    }

    // --- Suggest ---

    [TestMethod]
    public void Suggest_NullQuery_ReturnsFirstNEntries()
    {
        var catalog = Create(["Arial", "Calibri", "Cascadia Code", "Verdana"]);

        var results = catalog.Suggest(null, maxResults: 2).ToArray();

        Assert.AreEqual(2, results.Length);
        Assert.AreEqual("Arial", results[0]);
        Assert.AreEqual("Calibri", results[1]);
    }

    [TestMethod]
    public void Suggest_EmptyQuery_ReturnsFirstNEntries()
    {
        var catalog = Create(["Arial", "Calibri", "Cascadia Code"]);

        var results = catalog.Suggest("", maxResults: 5).ToArray();

        // All entries returned because maxResults exceeds the catalog size.
        Assert.AreEqual(3, results.Length);
    }

    [TestMethod]
    public void Suggest_StartsWithCaseInsensitive()
    {
        var catalog = Create(["Arial", "Calibri", "Cascadia Code", "Comic Sans MS", "Verdana"]);

        var results = catalog.Suggest("c", maxResults: 10).ToArray();

        CollectionAssert.AreEqual(new[] { "Calibri", "Cascadia Code", "Comic Sans MS" }, results);
    }

    [TestMethod]
    public void Suggest_TrimsQueryBeforeMatch()
    {
        var catalog = Create(["Arial", "Calibri", "Cascadia Code"]);

        var results = catalog.Suggest("  cas  ", maxResults: 10).ToArray();

        CollectionAssert.AreEqual(new[] { "Cascadia Code" }, results);
    }

    [TestMethod]
    public void Suggest_RespectsMaxResults()
    {
        var catalog = Create(Enumerable.Range(0, 50).Select(i => $"Font{i:D2}").ToArray());

        var results = catalog.Suggest("Font", maxResults: 5).ToArray();

        Assert.AreEqual(5, results.Length);
    }

    [TestMethod]
    public void Suggest_MaxResultsZero_ReturnsEmpty()
    {
        var catalog = Create(["Arial", "Calibri"]);

        var results = catalog.Suggest("a", maxResults: 0).ToArray();

        Assert.AreEqual(0, results.Length);
    }

    [TestMethod]
    public void Suggest_NoMatch_ReturnsEmpty()
    {
        var catalog = Create(["Arial", "Calibri"]);

        var results = catalog.Suggest("Z", maxResults: 10).ToArray();

        Assert.AreEqual(0, results.Length);
    }
}
