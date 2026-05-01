// <copyright file="PaneTreeView.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

using System.Collections.Generic;
using System.ComponentModel;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Code-only Avalonia <see cref="UserControl"/> that renders a
/// <see cref="PaneTree"/> as a nested <see cref="Grid"/> + draggable
/// <see cref="GridSplitter"/> hierarchy. Subscribes to
/// <see cref="PaneTree.StructureChanged"/> to rebuild its visual tree
/// and to <see cref="PaneTree.ActiveLeafChanged"/> to update the focus
/// indicator without a full rebuild.
/// </summary>
internal sealed class PaneTreeView : UserControl
{
    /// <summary>
    /// Per-pane minimum in device-independent pixels. Roughly a dozen
    /// cells of the default font and well above the GridSplitter's
    /// grabbable handle, so a pane cannot be shrunk into uselessness.
    /// </summary>
    private const double MinPanePx = 80.0;

    private static readonly Color DefaultActiveAccent = Color.FromRgb(0x60, 0xA5, 0xFA);

    private readonly PaneTree tree;
    private readonly AppSettings? settings;
    private readonly Dictionary<PaneLeaf, Border> leafBorders = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PaneTreeView"/>
    /// class.
    /// </summary>
    /// <param name="tree">The tree to render.</param>
    /// <param name="settings">Optional application settings used to
    /// derive the active-pane accent color. Pass <see langword="null"/>
    /// in tests — the view falls back to a fixed accent.</param>
    public PaneTreeView(PaneTree tree, AppSettings? settings)
    {
        this.tree = tree ?? throw new ArgumentNullException(nameof(tree));
        this.settings = settings;
        this.Focusable = true;

        this.tree.StructureChanged += this.OnStructureChanged;
        this.tree.ActiveLeafChanged += this.OnActiveLeafChanged;
        if (settings is not null)
        {
            settings.PropertyChanged += this.OnSettingsPropertyChanged;
        }

        this.RebuildVisual();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLogicalTree(global::Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        this.tree.StructureChanged -= this.OnStructureChanged;
        this.tree.ActiveLeafChanged -= this.OnActiveLeafChanged;
        if (this.settings is not null)
        {
            this.settings.PropertyChanged -= this.OnSettingsPropertyChanged;
        }
    }

    private static bool IsVerticalDivider(PaneOrientation orientation)
    {
        // A Vertical orientation = vertical divider = children side by side (columns).
        return orientation == PaneOrientation.Vertical;
    }

    private void OnStructureChanged()
    {
        this.RebuildVisual();
    }

    private void OnActiveLeafChanged(PaneLeaf leaf)
    {
        this.ApplyActiveHighlight();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ForegroundColor))
        {
            this.ApplyActiveHighlight();
        }
    }

    private void RebuildVisual()
    {
        // Detach hosts from old borders so they can be re-parented freshly.
        foreach (var b in this.leafBorders.Values)
        {
            b.Child = null;
        }

        this.leafBorders.Clear();
        this.Content = this.BuildNodeVisual(this.tree.Root);
        this.ApplyActiveHighlight();
    }

    private Control BuildNodeVisual(PaneNode node)
    {
        if (node is PaneLeaf leaf)
        {
            return this.BuildLeafVisual(leaf);
        }

        return this.BuildSplitVisual((PaneSplit)node);
    }

    private Control BuildLeafVisual(PaneLeaf leaf)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Padding = default,
            MinWidth = MinPanePx,
            MinHeight = MinPanePx,
            Child = leaf.Content.Host,
        };

        border.AddHandler(
            InputElement.PointerPressedEvent,
            (s, e) => this.tree.SetActive(leaf),
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        this.leafBorders[leaf] = border;
        return border;
    }

    private Control BuildSplitVisual(PaneSplit split)
    {
        var grid = new Grid();
        bool vertical = IsVerticalDivider(split.Orientation);

        var first = this.BuildNodeVisual(split.First);
        var second = this.BuildNodeVisual(split.Second);
        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false,
        };

        if (vertical)
        {
            var c0 = new ColumnDefinition(split.Ratio, GridUnitType.Star) { MinWidth = MinPanePx };
            var cs = new ColumnDefinition(4, GridUnitType.Pixel);
            var c1 = new ColumnDefinition(1 - split.Ratio, GridUnitType.Star) { MinWidth = MinPanePx };
            grid.ColumnDefinitions.Add(c0);
            grid.ColumnDefinitions.Add(cs);
            grid.ColumnDefinitions.Add(c1);

            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);

            splitter.Width = 4;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Columns;
            splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);

            splitter.DragCompleted += (_, _) =>
            {
                double total = c0.ActualWidth + c1.ActualWidth;
                if (total > 0)
                {
                    split.Ratio = c0.ActualWidth / total;
                    this.tree.NotifyRatioChanged();
                }
            };
        }
        else
        {
            var r0 = new RowDefinition(split.Ratio, GridUnitType.Star) { MinHeight = MinPanePx };
            var rs = new RowDefinition(4, GridUnitType.Pixel);
            var r1 = new RowDefinition(1 - split.Ratio, GridUnitType.Star) { MinHeight = MinPanePx };
            grid.RowDefinitions.Add(r0);
            grid.RowDefinitions.Add(rs);
            grid.RowDefinitions.Add(r1);

            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);

            splitter.Height = 4;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Rows;
            splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);

            splitter.DragCompleted += (_, _) =>
            {
                double total = r0.ActualHeight + r1.ActualHeight;
                if (total > 0)
                {
                    split.Ratio = r0.ActualHeight / total;
                    this.tree.NotifyRatioChanged();
                }
            };
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }

    private void ApplyActiveHighlight()
    {
        // The focus accent only makes sense when the user actually has multiple
        // panes to disambiguate. With a single leaf there is nothing to compare
        // against, so painting the accent reads as a stray border around the
        // whole terminal. Keep every leaf transparent in that case.
        bool showAccent = this.leafBorders.Count > 1;
        var accent = showAccent ? this.GetActiveAccentBrush() : Brushes.Transparent;
        foreach (var kv in this.leafBorders)
        {
            bool active = showAccent && ReferenceEquals(kv.Key, this.tree.ActiveLeaf);
            kv.Value.BorderBrush = active ? accent : Brushes.Transparent;
        }
    }

    private IBrush GetActiveAccentBrush()
    {
        if (this.settings is null)
        {
            return new SolidColorBrush(this.ResolveActiveAccentColor());
        }

        int rgb = this.settings.ForegroundColor;
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);
        return new SolidColorBrush(Color.FromArgb(0xA0, r, g, b));
    }

    private Color ResolveActiveAccentColor()
    {
        if (this.TryGetResource("AccentPrimaryBrush", this.ActualThemeVariant, out var value))
        {
            if (value is ISolidColorBrush brush)
            {
                return brush.Color;
            }

            if (value is Color color)
            {
                return color;
            }
        }

        return DefaultActiveAccent;
    }
}
