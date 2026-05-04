// <copyright file="DragPreviewWindow.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// A small, borderless, topmost floating window displayed while a tab is
/// being dragged outside its source window. Shows the tab title and an icon
/// indicating whether releasing will create a new window or merge into an
/// existing one.
/// </summary>
internal sealed class DragPreviewWindow : Window
{
    private const double PreviewWidth = 200;
    private const double PreviewHeight = 36;

    private readonly TextBlock iconBlock;
    private readonly TabTitlePresenter titleBlock;
    private bool isMergeMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="DragPreviewWindow"/> class.
    /// </summary>
    /// <param name="tabTitle">Title of the tab being dragged.</param>
    public DragPreviewWindow(string tabTitle)
    {
        this.WindowDecorations = WindowDecorations.None;
        this.ShowInTaskbar = false;
        this.CanResize = false;
        this.Topmost = true;
        this.Focusable = false;
        this.ShowActivated = false;
        this.Width = PreviewWidth;
        this.Height = PreviewHeight;
        this.MinWidth = PreviewWidth;
        this.MinHeight = PreviewHeight;
        this.MaxWidth = PreviewWidth;
        this.MaxHeight = PreviewHeight;
        this.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        this.Background = Brushes.Transparent;

        var previewBackground = this.ResolveThemeBrush("SurfaceOverlayBrush", Color.FromArgb(0xD0, 0x30, 0x30, 0x30));
        var previewForeground = this.ResolveThemeBrush("TextPrimaryBrush", Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

        this.iconBlock = new TextBlock
        {
            Text = "\u29C9", // ⧉ — new-window icon
            Foreground = previewForeground,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0),
        };

        this.titleBlock = new TabTitlePresenter
        {
            Text = tabTitle,
            ForegroundBrush = previewForeground,
            TitleFontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = PreviewWidth - 50,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { this.iconBlock, this.titleBlock },
        };

        this.Content = new Border
        {
            Background = previewBackground,
            CornerRadius = new CornerRadius(6),
            Child = panel,
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// Gets or sets a value indicating whether the preview shows "merge"
    /// mode (drop onto existing window) vs "new window" mode.
    /// </summary>
    public bool IsMergeMode
    {
        get => this.isMergeMode;
        set
        {
            if (this.isMergeMode == value)
            {
                return;
            }

            this.isMergeMode = value;

            // ⤓ merge indicator, ⧉ new-window indicator
            this.iconBlock.Text = value ? "\u2913" : "\u29C9";
        }
    }

    /// <summary>
    /// Moves the preview window so that it appears offset from the given
    /// screen position (typically the cursor position).
    /// </summary>
    /// <param name="screenPos">Cursor position in screen pixels.</param>
    public void MoveToScreenPosition(PixelPoint screenPos)
    {
        try
        {
            this.Position = new PixelPoint(screenPos.X + 12, screenPos.Y + 16);
        }
        catch
        {
            // Position may throw on some platforms before the window is shown.
        }
    }

    private IBrush ResolveThemeBrush(string key, Color fallback)
    {
        if (this.TryGetResource(key, this.ActualThemeVariant, out var value))
        {
            if (value is IBrush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(fallback);
    }
}
