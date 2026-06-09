using System.Xml.Linq;

namespace Hypersonic.Tests.Views;

[TestClass]
public class NowPlayingPageLayoutTests
{
    [TestMethod]
    public void QueueRow_UsesStableDurationColumn()
    {
        var rowGrid = LoadQueueRowGrid();

        var widths = rowGrid
            .Elements()
            .Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .Where(e => e.Name.LocalName == "ColumnDefinition")
            .Select(e => e.Attribute("Width")?.Value)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "Auto", "*", "240", "60", "Auto" },
            widths,
            "Queue row columns should reserve fixed space for runtime so earlier text columns do not shift per row.");

        var durationText = rowGrid
            .Descendants()
            .Single(e =>
                e.Name.LocalName == "TextBlock"
                && e.Attribute("Grid.Column")?.Value == "3"
                && e.Attribute("Text")?.Value.Contains("SecondsToTimeConverter") == true);

        Assert.AreEqual("Stretch", durationText.Attribute("HorizontalAlignment")?.Value);
        Assert.AreEqual("Right", durationText.Attribute("TextAlignment")?.Value);
    }

    private static XElement LoadQueueRowGrid()
    {
        var xaml = XDocument.Load(FindRepositoryFile("Hypersonic", "Views", "NowPlayingPage.xaml"));
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var queueTemplate = xaml
            .Descendants()
            .Single(e =>
                e.Name.LocalName == "DataTemplate"
                && e.Attribute(xamlNamespace + "DataType")?.Value == "models:QueueItem");

        return queueTemplate
            .Descendants()
            .Single(e =>
                e.Name.LocalName == "Grid"
                && e.Attribute("Padding")?.Value == "8,6"
                && e.Attribute("Height")?.Value == "56");
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not find repository file: {Path.Combine(pathParts)}");
        return string.Empty;
    }
}
