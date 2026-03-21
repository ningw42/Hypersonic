using Hypersonic.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Helpers;

public class TrackListTemplateSelector : DataTemplateSelector
{
    public DataTemplate DiscHeaderTemplate { get; set; } = null!;
    public DataTemplate TrackTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item is DiscHeaderItem ? DiscHeaderTemplate : TrackTemplate;
    }
}
