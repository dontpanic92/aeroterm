// <copyright file="GitDiffLineNumberMargin.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

/// <summary>
/// Displays original source line numbers for one aligned Git diff editor.
/// </summary>
internal sealed class GitDiffLineNumberMargin : AbstractMargin
{
    private const double HorizontalPadding = 6;
    private readonly Func<GitDiffHighlightKind, IBrush> highlightBrushProvider;
    private IReadOnlyList<int?> lineNumbers = Array.Empty<int?>();
    private IReadOnlyList<GitDiffHighlightRange> highlights = Array.Empty<GitDiffHighlightRange>();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitDiffLineNumberMargin"/> class.
    /// </summary>
    /// <param name="highlightBrushProvider">Resolves a background brush for a diff highlight kind.</param>
    internal GitDiffLineNumberMargin(Func<GitDiffHighlightKind, IBrush> highlightBrushProvider)
    {
        this.highlightBrushProvider = highlightBrushProvider
            ?? throw new ArgumentNullException(nameof(highlightBrushProvider));
        this.IsHitTestVisible = false;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var textView = this.TextView;
        if (textView is not { VisualLinesValid: true })
        {
            return;
        }

        var foreground = this.GetValue(TextBlock.ForegroundProperty);
        var typeface = new Typeface(this.GetValue(TextBlock.FontFamilyProperty));
        var fontSize = this.GetValue(TextBlock.FontSizeProperty);
        foreach (var visualLine in textView.VisualLines)
        {
            var displayedLine = visualLine.FirstDocumentLine.LineNumber;
            var top = visualLine.VisualTop - textView.VerticalOffset;
            var highlight = this.FindHighlight(displayedLine);
            if (highlight is not null)
            {
                context.DrawRectangle(
                    this.highlightBrushProvider(highlight.Value),
                    null,
                    new Rect(0, top, this.Bounds.Width, visualLine.Height));
            }

            if (displayedLine > this.lineNumbers.Count ||
                this.lineNumbers[displayedLine - 1] is not { } sourceLine)
            {
                continue;
            }

            var formattedText = new FormattedText(
                sourceLine.ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                foreground);
            var y = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0],
                VisualYPosition.TextTop) - textView.VerticalOffset;
            context.DrawText(
                formattedText,
                new Point(this.Bounds.Width - HorizontalPadding - formattedText.Width, y));
        }
    }

    /// <summary>
    /// Updates the displayed source line numbers and highlights.
    /// </summary>
    /// <param name="newLineNumbers">Original source line numbers for displayed rows.</param>
    /// <param name="newHighlights">Highlighted displayed-row ranges.</param>
    internal void SetContent(
        IReadOnlyList<int?> newLineNumbers,
        IReadOnlyList<GitDiffHighlightRange> newHighlights)
    {
        this.lineNumbers = newLineNumbers;
        this.highlights = newHighlights;
        this.InvalidateMeasure();
        this.InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var maxLineNumber = this.lineNumbers
            .Where(lineNumber => lineNumber.HasValue)
            .Select(lineNumber => lineNumber!.Value)
            .DefaultIfEmpty(99)
            .Max();
        var digits = Math.Max(2, maxLineNumber.ToString(CultureInfo.CurrentCulture).Length);
        var fontSize = this.GetValue(TextBlock.FontSizeProperty);
        return new Size((digits * fontSize * 0.65) + (HorizontalPadding * 2), 0);
    }

    private GitDiffHighlightKind? FindHighlight(int displayedLine)
    {
        foreach (var range in this.highlights)
        {
            if (displayedLine >= range.StartLine &&
                displayedLine < range.StartLine + range.LineCount)
            {
                return range.Kind;
            }
        }

        return null;
    }
}
