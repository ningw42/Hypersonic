using CommunityToolkit.Mvvm.ComponentModel;

namespace Hypersonic.Models;

public partial class DiscHeaderItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public int DiscNumber { get; set; }

    [ObservableProperty]
    private bool isExpanded = true;
}
