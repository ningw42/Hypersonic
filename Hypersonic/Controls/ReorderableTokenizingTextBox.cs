using System.Collections;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.WinUI.Controls;

using Hypersonic.Core.Helpers;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Hypersonic.Controls;

/// <summary>
/// A <see cref="TokenizingTextBox"/> that adds drag-to-reorder over its chips. Reorder is owned explicitly:
/// the drop mutates the bound collection as a Remove+Insert (which the base re-projects, keeping the trailing
/// editor last) — <see cref="ListViewBase.CanReorderItems"/> is deliberately NOT used. A chip is identified as
/// an item present in the bound <see cref="ReorderItemsSource"/>; the internal editor placeholder is not,
/// so it can never be dragged or become a drop target.
/// </summary>
public sealed partial class ReorderableTokenizingTextBox : TokenizingTextBox
{
    private object? _dragged;

    public ReorderableTokenizingTextBox()
    {
        CanDragItems = true;
        AllowDrop = true;
        DragItemsStarting += OnDragItemsStarting;
        DragItemsCompleted += OnDragItemsCompleted;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    /// <summary>Raised after a drag reorder with the new full chip order (the bound item objects).</summary>
    public event TypedEventHandler<ReorderableTokenizingTextBox, IReadOnlyList<object>>? ItemsReordered;

    /// <summary>The original bound collection to mutate when chips are reordered.</summary>
    public IList? ReorderItemsSource { get; set; }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 1 && ReorderItemsSource is { } source && source.Contains(e.Items[0]))
        {
            _dragged = e.Items[0];
        }
        else
        {
            _dragged = null;
            e.Cancel = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_dragged is not null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dragged is null || ReorderItemsSource is not { } chips)
        {
            return;
        }

        e.Handled = true;

        var from = chips.IndexOf(_dragged);
        if (from < 0)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var bounds = new List<(double Left, double Top, double Width, double Height)>(chips.Count);
        foreach (var chip in chips)
        {
            if (ReferenceEquals(chip, _dragged))
            {
                continue;
            }

            if (ContainerFromItem(chip) is FrameworkElement fe)
            {
                var origin = fe.TransformToVisual(this).TransformPoint(new Point(0, 0));
                bounds.Add((origin.X, origin.Y, fe.ActualWidth, fe.ActualHeight));
            }
        }

        var target = ReorderDropIndex.Resolve(pointer.X, pointer.Y, bounds);
        target = System.Math.Clamp(target, 0, chips.Count - 1);

        if (target != from)
        {
            chips.RemoveAt(from);
            chips.Insert(target, _dragged);
            ItemsReordered?.Invoke(this, chips.Cast<object>().ToList());
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Clear on every completion path — a successful internal drop, a cancel, or a drop outside this control.
        _dragged = null;
    }
}
