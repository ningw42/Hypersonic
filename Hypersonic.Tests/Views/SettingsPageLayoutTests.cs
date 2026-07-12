using System.Xml.Linq;

namespace Hypersonic.Tests.Views;

[TestClass]
public class SettingsPageLayoutTests
{
    [TestMethod]
    public void LyricsProviderBox_UsesCommaTokenDelimiterAndSharedReorderSource()
    {
        var xaml = XDocument.Load(FindRepositoryFile("Hypersonic", "Views", "SettingsPage.xaml"));
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var providerBox = xaml
            .Descendants()
            .Single(e => e.Attribute(xamlNamespace + "Name")?.Value == "LyricsProviderBox");

        Assert.AreEqual(",", providerBox.Attribute("TokenDelimiter")?.Value);
        Assert.AreEqual(
            providerBox.Attribute("ItemsSource")?.Value,
            providerBox.Attribute("ReorderItemsSource")?.Value);
    }

    [TestMethod]
    public void LyricsProviderBox_UsesTwoPixelTokenSpacing()
    {
        var xaml = XDocument.Load(FindRepositoryFile("Hypersonic", "Views", "SettingsPage.xaml"));
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var providerBox = xaml
            .Descendants()
            .Single(e => e.Attribute(xamlNamespace + "Name")?.Value == "LyricsProviderBox");

        Assert.AreEqual("2", providerBox.Attribute("TokenSpacing")?.Value);
    }

    [TestMethod]
    public void LyricsProviderBox_UsesDerivedItemsPanelSpacingBindings()
    {
        var xaml = XDocument.Load(FindRepositoryFile("Hypersonic", "Views", "SettingsPage.xaml"));
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var controlsNamespace = XNamespace.Get("using:CommunityToolkit.WinUI.Controls");
        var hypersonicControlsNamespace = XNamespace.Get("using:Hypersonic.Controls");
        var toolkitNamespace = XNamespace.Get("using:CommunityToolkit.WinUI");

        var providerBox = xaml
            .Descendants()
            .Single(e => e.Attribute(xamlNamespace + "Name")?.Value == "LyricsProviderBox");
        var itemsPanel = providerBox
            .Elements(hypersonicControlsNamespace + "ReorderableTokenizingTextBox.ItemsPanel")
            .Single();
        var wrapPanel = itemsPanel.Descendants(controlsNamespace + "WrapPanel").Single();

        Assert.AreEqual(
            "hcontrols:ReorderableTokenizingTextBox",
            wrapPanel.Attribute(toolkitNamespace + "FrameworkElementExtensions.AncestorType")?.Value);
        Assert.AreEqual(
            "{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}",
            wrapPanel.Attribute("HorizontalSpacing")?.Value);
        Assert.AreEqual(
            "{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}",
            wrapPanel.Attribute("VerticalSpacing")?.Value);
        Assert.AreEqual("Last", wrapPanel.Attribute("StretchChild")?.Value);
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
