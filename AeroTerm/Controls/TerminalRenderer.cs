// <copyright file="TerminalRenderer.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using AeroTerm.Controls.Terminal;
using AeroTerm.Pty;
using SkiaSharp;

/// <summary>
/// Paints the terminal grid, cursor, and IME preedit overlay onto a Skia canvas.
/// </summary>
internal sealed class TerminalRenderer : IDisposable
{
    private readonly FontFallbackChain fontChain;
    private readonly LigatureTextShaper ligatureTextShaper;
    private readonly EditorTextInputMethodClient imeClient;
    private readonly SymbolGlyphRenderer symbolGlyphRenderer = new();
    private readonly SKPaint backgroundPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint textPaint = new() { IsAntialias = true };
    private readonly SKPaint underlinePaint = new() { StrokeWidth = 1, IsAntialias = true };
    private readonly SKPaint undercurlPaint = new() { StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint cursorPaint = new() { BlendMode = SKBlendMode.Difference, Color = SKColors.White };
    private readonly SKPaint preeditUnderlinePaint = new() { StrokeWidth = 2, IsAntialias = true, Color = SKColors.White };
    private readonly SKPath undercurlPath = new();
    private readonly List<PlainGlyphEntry> plainGlyphBatch = new();
    private readonly StringBuilder batchTextBuilder = new();
    private readonly List<ResolvedTypefaceRun> resolvedRuns = new();
    private readonly StringBuilder runTextBuilder = new();
    private readonly List<TextCellSpan> runCellSpans = new();
    private readonly List<TextCellSpan> allCellSpans = new();
    private readonly SKPaint selectionPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint searchMatchPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint searchActiveBorderPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private SKFont textFont = new() { Subpixel = true, LinearMetrics = false };
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalRenderer"/> class.
    /// </summary>
    /// <param name="fontChain">The font fallback chain.</param>
    /// <param name="ligatureTextShaper">The ligature text shaper.</param>
    /// <param name="imeClient">The IME client for preedit state.</param>
    public TerminalRenderer(
        FontFallbackChain fontChain,
        LigatureTextShaper ligatureTextShaper,
        EditorTextInputMethodClient imeClient)
    {
        this.fontChain = fontChain;
        this.ligatureTextShaper = ligatureTextShaper;
        this.imeClient = imeClient;
    }

    /// <summary>
    /// Renders the terminal grid onto the given canvas.
    /// </summary>
    /// <param name="canvas">The Skia canvas to paint on.</param>
    /// <param name="screen">The current screen snapshot.</param>
    /// <param name="textParam">The current text layout parameters.</param>
    /// <param name="modeInfo">The current mode info for cursor rendering.</param>
    /// <param name="enableLigature">Whether ligature shaping is enabled.</param>
    /// <param name="backgroundAlpha">The alpha channel for the default background.</param>
    /// <param name="shouldDrawCursor">Whether the cursor should be drawn.</param>
    /// <param name="topInset">Vertical offset in pixels to push grid rendering
    /// down from the canvas origin, leaving the inset area showing only the
    /// cleared background. Used for the floating title-bar blur effect.</param>
    /// <param name="selection">Optional active text selection to overlay, or <c>null</c>.</param>
    /// <param name="selectionColor">Fill color for the selection overlay. Ignored when <paramref name="selection"/> is null or empty.</param>
    /// <param name="selectionRowOffset">Absolute-row offset corresponding to
    /// screen row 0. Selection coordinates are in absolute-row space; the
    /// renderer projects them to screen rows as
    /// <c>screenRow = absRow - selectionRowOffset</c>.</param>
    /// <param name="hyperlinkRun">Optional OSC 8 hyperlink run to underline as a hover affordance, or <c>null</c>.</param>
    /// <param name="searchMatches">Optional visible search-overlay matches
    /// to highlight (projected into <see cref="Pty.Screen"/> row coords).
    /// Only on-screen matches should be passed.</param>
    public void Render(
        SKCanvas canvas,
        Screen screen,
        TextLayoutParameters textParam,
        ModeInfo? modeInfo,
        bool enableLigature,
        byte backgroundAlpha,
        bool shouldDrawCursor,
        float topInset = 0,
        TerminalSelection? selection = null,
        SKColor selectionColor = default,
        int selectionRowOffset = 0,
        HyperlinkRun? hyperlinkRun = null,
        IReadOnlyList<VisibleMatch>? searchMatches = null)
    {
        canvas.Clear(GetSkColor(screen.BackgroundColor, backgroundAlpha));

        canvas.Save();
        canvas.Translate(0, topInset);

        var cells = screen.Cells;
        var palette = screen.Palette;
        int rows = cells.GetLength(0);
        int cols = cells.GetLength(1);

        this.textFont.Size = textParam.SkiaFontSize;

        // Paint backgrounds
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                int cellBg = cells[i, j].ResolveBackground(palette);
                if (cellBg != screen.BackgroundColor || cells[i, j].Reverse)
                {
                    float x = j * textParam.CharWidth;
                    float y = i * textParam.LineHeight;
                    int color = cells[i, j].Reverse
                        ? cells[i, j].ResolveForeground(palette)
                        : cellBg;
                    this.backgroundPaint.Color = GetSkColor(color);
                    canvas.DrawRect(x, y, textParam.CharWidth, textParam.LineHeight, this.backgroundPaint);
                }
            }
        }

        // Paint selection overlay between backgrounds and text so glyphs stay
        // legible atop the tint. Selection coordinates are in absolute-row
        // space; project to screen rows via selectionRowOffset.
        if (selection is not null && !selection.IsEmpty)
        {
            this.selectionPaint.Color = selectionColor;
            var (sr, sc, er, ec) = selection.GetNormalizedRange();
            int srScreen = sr - selectionRowOffset;
            int erScreen = er - selectionRowOffset;

            // Clip to the visible screen rows.
            int firstScreen = Math.Max(0, srScreen);
            int lastScreen = Math.Min(rows - 1, erScreen);
            for (int i = firstScreen; i <= lastScreen; i++)
            {
                int startCol = i == srScreen ? Math.Clamp(sc, 0, cols - 1) : 0;
                int endCol = i == erScreen ? Math.Clamp(ec, 0, cols - 1) : cols - 1;
                if (endCol < startCol)
                {
                    continue;
                }

                float x = startCol * textParam.CharWidth;
                float y = i * textParam.LineHeight;
                float w = (endCol - startCol + 1) * textParam.CharWidth;
                canvas.DrawRect(x, y, w, textParam.LineHeight, this.selectionPaint);
            }
        }

        // Paint search match overlays. Inactive matches get a translucent
        // tint; the active match gets a stronger tint plus a 1px border
        // in the screen foreground color. Drawn before text so glyphs
        // stay legible.
        if (searchMatches is not null && searchMatches.Count > 0)
        {
            SKColor baseTint = selectionColor.Alpha > 0
                ? selectionColor
                : new SKColor(0x39, 0x66, 0xCC, 0x50);
            var inactiveFill = new SKColor(baseTint.Red, baseTint.Green, baseTint.Blue, 60);
            var activeFill = new SKColor(baseTint.Red, baseTint.Green, baseTint.Blue, 130);
            var borderColor = GetSkColor(screen.ForegroundColor);

            for (int idx = 0; idx < searchMatches.Count; idx++)
            {
                var m = searchMatches[idx];
                if (m.ScreenRow < 0 || m.ScreenRow >= rows || m.CellLength <= 0)
                {
                    continue;
                }

                int startCol = Math.Clamp(m.StartCol, 0, cols - 1);
                int endCol = Math.Clamp(m.StartCol + m.CellLength - 1, startCol, cols - 1);
                float x = startCol * textParam.CharWidth;
                float y = m.ScreenRow * textParam.LineHeight;
                float w = (endCol - startCol + 1) * textParam.CharWidth;

                this.searchMatchPaint.Color = m.IsActive ? activeFill : inactiveFill;
                canvas.DrawRect(x, y, w, textParam.LineHeight, this.searchMatchPaint);

                if (m.IsActive)
                {
                    this.searchActiveBorderPaint.Color = borderColor;
                    canvas.DrawRect(x + 0.5f, y + 0.5f, w - 1, textParam.LineHeight - 1, this.searchActiveBorderPaint);
                }
            }
        }

        // Paint foreground text
        for (int i = 0; i < rows; i++)
        {
            int j = 0;
            while (j < cols)
            {
                int cellRangeStart = j;
                int cellRangeEnd = j;
                Cell startCell = cells[i, cellRangeStart];
                while (cellRangeEnd < cols)
                {
                    Cell cell = cells[i, cellRangeEnd];
                    if (cell.Character is not null
                        && (cell.ForegroundColor != startCell.ForegroundColor
                            || cell.BackgroundColor != startCell.BackgroundColor
                            || cell.SpecialColor != startCell.SpecialColor
                            || cell.Italic != startCell.Italic
                            || cell.Bold != startCell.Bold
                            || cell.Reverse != startCell.Reverse
                            || cell.Undercurl != startCell.Undercurl
                            || cell.Underline != startCell.Underline
                            || cell.DoubleUnderline != startCell.DoubleUnderline
                            || cell.Strikethrough != startCell.Strikethrough))
                    {
                        break;
                    }

                    cellRangeEnd++;
                }

                j = cellRangeEnd;

                this.DrawCellRange(canvas, cells, palette, i, cellRangeStart, cellRangeEnd, textParam, enableLigature);
            }
        }

        // Draw hyperlink hover underline overlay. This is purely a rendering-time
        // affordance — we don't mutate cell style. Drawn after text so it sits on
        // top even when the cell already has other decorations.
        if (hyperlinkRun is { } run
            && run.Row >= 0 && run.Row < rows
            && run.EndCol >= run.StartCol)
        {
            int sc = Math.Clamp(run.StartCol, 0, cols - 1);
            int ec = Math.Clamp(run.EndCol, 0, cols - 1);
            if (ec >= sc)
            {
                int fg = cells[run.Row, sc].Reverse
                    ? cells[run.Row, sc].ResolveBackground(palette)
                    : cells[run.Row, sc].ResolveForeground(palette);
                this.underlinePaint.Color = GetSkColor(fg);
                float ulY = ((run.Row + 1) * textParam.LineHeight) - 1;
                float sx = sc * textParam.CharWidth;
                float ex = (ec + 1) * textParam.CharWidth;
                canvas.DrawLine(sx, ulY, ex, ulY, this.underlinePaint);
            }
        }

        // Draw cursor
        if (shouldDrawCursor)
        {
            this.DrawCursor(canvas, cells, screen, modeInfo, textParam);
        }

        // Draw preedit (IME composition) overlay
        this.DrawPreedit(canvas, screen, textParam);

        canvas.Restore();
    }

    /// <summary>
    /// Discards any cached rendering state. Currently a no-op since the
    /// renderer paints directly on the canvas, but retained for API
    /// compatibility.
    /// </summary>
    public void DiscardBackbuffer()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!this.isDisposed)
        {
            this.backgroundPaint.Dispose();
            this.textPaint.Dispose();
            this.underlinePaint.Dispose();
            this.undercurlPaint.Dispose();
            this.cursorPaint.Dispose();
            this.preeditUnderlinePaint.Dispose();
            this.selectionPaint.Dispose();
            this.searchMatchPaint.Dispose();
            this.searchActiveBorderPaint.Dispose();
            this.textFont.Dispose();
            this.undercurlPath.Dispose();
            this.symbolGlyphRenderer.Dispose();
            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Converts a packed RGB integer to an <see cref="SKColor"/>.
    /// </summary>
    /// <param name="color">The color as (R &lt;&lt; 16) | (G &lt;&lt; 8) | B.</param>
    /// <param name="alpha">The alpha channel value.</param>
    /// <returns>The corresponding <see cref="SKColor"/>.</returns>
    internal static SKColor GetSkColor(int color, byte alpha = 255)
    {
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);

        return new SKColor(r, g, b, alpha);
    }

    private static bool TryGetClusterStartColumn(ReadOnlySpan<TextCellSpan> cellSpans, int clusterStart, int clusterEnd, out int startColumn)
    {
        foreach (var cellSpan in cellSpans)
        {
            bool overlaps = cellSpan.TextStart < clusterEnd && clusterStart < (cellSpan.TextStart + cellSpan.TextLength);
            if (overlaps)
            {
                startColumn = cellSpan.ColumnStart;
                return true;
            }
        }

        startColumn = 0;
        return false;
    }

    private static int GetCharWidth(Cell[,] screen, int row, int col)
    {
        if (col >= screen.GetLength(1) - 1)
        {
            return 1;
        }

        if (screen[row, col + 1].Character is null)
        {
            return 2;
        }

        return 1;
    }

    private static bool IsSymbolCell(Cell[,] cells, int row, int col)
    {
        var cell = cells[row, col];
        if (cell.Character is null || cell.Character.Length == 0)
        {
            return false;
        }

        // Surrogate pairs in symbol ranges (Legacy Computing lives in the
        // SMP) need ConvertToUtf32; non-surrogate BMP chars take the fast
        // path on the high byte to avoid the surrogate-handling cost.
        char first = cell.Character[0];
        if (!char.IsHighSurrogate(first))
        {
            return SymbolGlyphRanges.Handles(first);
        }

        if (cell.Character.Length < 2)
        {
            return false;
        }

        int cp = char.ConvertToUtf32(first, cell.Character[1]);
        return SymbolGlyphRanges.Handles(cp);
    }

    private void DrawCellRange(
        SKCanvas canvas,
        Cell[,] cells,
        PaletteSnapshot palette,
        int row,
        int colStart,
        int colEnd,
        TextLayoutParameters textParam,
        bool enableLigature)
    {
        bool bold = cells[row, colStart].Bold;
        bool italic = cells[row, colStart].Italic;
        UnderlineStyle underlineStyle = cells[row, colStart].UnderlineStyle;
        bool strikethrough = cells[row, colStart].Strikethrough;
        int specialColor = cells[row, colStart].SpecialColor;
        int foregroundColor = cells[row, colStart].Reverse
            ? cells[row, colStart].ResolveBackground(palette)
            : cells[row, colStart].ResolveForeground(palette);
        int underlineColor = specialColor != 0 ? specialColor : foregroundColor;

        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var styledTypeface = this.fontChain.GetStyledTypeface(weight, slant);
        this.textPaint.Color = GetSkColor(foregroundColor);
        this.textFont.Typeface = styledTypeface;

        float baselineY = (row * textParam.LineHeight) + (textParam.LineHeight * 0.8f);

        if (enableLigature)
        {
            this.textFont.Embolden = false;
            this.DrawCellRangeWithSymbols(canvas, cells, row, colStart, colEnd, weight, slant, baselineY, bold, textParam, ligatures: true, foregroundColor, styledTypeface);
        }
        else
        {
            this.textFont.Embolden = bold;
            this.DrawCellRangeWithSymbols(canvas, cells, row, colStart, colEnd, weight, slant, baselineY, bold, textParam, ligatures: false, foregroundColor, styledTypeface);
        }

        // Draw underline decoration (single / double / curly)
        switch (underlineStyle)
        {
            case UnderlineStyle.Single:
            case UnderlineStyle.Dotted:
            case UnderlineStyle.Dashed:
                this.underlinePaint.Color = GetSkColor(underlineColor);
                {
                    float ulY = ((row + 1) * textParam.LineHeight) - 1;
                    canvas.DrawLine(colStart * textParam.CharWidth, ulY, colEnd * textParam.CharWidth, ulY, this.underlinePaint);
                }

                break;

            case UnderlineStyle.Double:
                this.underlinePaint.Color = GetSkColor(underlineColor);
                {
                    float ulY1 = ((row + 1) * textParam.LineHeight) - 3;
                    float ulY2 = ((row + 1) * textParam.LineHeight) - 1;
                    float sx = colStart * textParam.CharWidth;
                    float ex = colEnd * textParam.CharWidth;
                    canvas.DrawLine(sx, ulY1, ex, ulY1, this.underlinePaint);
                    canvas.DrawLine(sx, ulY2, ex, ulY2, this.underlinePaint);
                }

                break;

            case UnderlineStyle.Curly:
                this.undercurlPaint.Color = GetSkColor(underlineColor);
                {
                    float curlY = ((row + 1) * textParam.LineHeight) - 2;
                    this.undercurlPath.Reset();
                    float startX = colStart * textParam.CharWidth;
                    float endX = colEnd * textParam.CharWidth;
                    this.undercurlPath.MoveTo(startX, curlY);
                    for (float cx = startX; cx < endX; cx += 4)
                    {
                        this.undercurlPath.QuadTo(cx + 2, curlY - 2, cx + 4, curlY);
                    }

                    canvas.DrawPath(this.undercurlPath, this.undercurlPaint);
                }

                break;
        }

        // Draw strikethrough (always uses foreground color).
        if (strikethrough)
        {
            this.underlinePaint.Color = GetSkColor(foregroundColor);
            float sY = (row * textParam.LineHeight) + (textParam.LineHeight * 0.55f);
            canvas.DrawLine(colStart * textParam.CharWidth, sY, colEnd * textParam.CharWidth, sY, this.underlinePaint);
        }
    }

    private void DrawCellRangeWithSymbols(
        SKCanvas canvas,
        Cell[,] cells,
        int row,
        int colStart,
        int colEnd,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant,
        float baselineY,
        bool bold,
        TextLayoutParameters textParam,
        bool ligatures,
        int foregroundColor,
        SKTypeface styledTypeface)
    {
        // Walk the column range, splitting consecutive cells into
        // alternating "text" and "symbol" sub-runs. Text sub-runs are
        // dispatched to the existing font path so all batching/ligature
        // shaping is preserved. Symbol cells are painted by the
        // SymbolGlyphRenderer at exact cell-rect geometry, bypassing the
        // font's per-glyph natural advance which is what creates the
        // sub-pixel disconnection gap that motivates this code.
        int j = colStart;
        SKColor symbolColor = GetSkColor(foregroundColor);
        while (j < colEnd)
        {
            int textStart = j;
            while (j < colEnd && !IsSymbolCell(cells, row, j))
            {
                j++;
            }

            int textEnd = j;
            if (textEnd > textStart)
            {
                if (ligatures)
                {
                    this.DrawLigatureTextRange(canvas, cells, row, textStart, textEnd, weight, slant, baselineY, bold, textParam);
                }
                else
                {
                    this.DrawPlainTextRange(canvas, cells, row, textStart, textEnd, styledTypeface, weight, slant, baselineY, textParam);
                }
            }

            while (j < colEnd && IsSymbolCell(cells, row, j))
            {
                var cell = cells[row, j];
                int codePoint = char.ConvertToUtf32(cell.Character!, 0);
                var rect = new SKRect(
                    j * textParam.CharWidth,
                    row * textParam.LineHeight,
                    (j + 1) * textParam.CharWidth,
                    (row + 1) * textParam.LineHeight);
                if (!this.symbolGlyphRenderer.TryDraw(canvas, codePoint, rect, symbolColor))
                {
                    // No programmatic implementation: fall back to the
                    // font for this single cell so we never lose a glyph.
                    if (ligatures)
                    {
                        this.DrawLigatureTextRange(canvas, cells, row, j, j + 1, weight, slant, baselineY, bold, textParam);
                    }
                    else
                    {
                        this.DrawPlainTextRange(canvas, cells, row, j, j + 1, styledTypeface, weight, slant, baselineY, textParam);
                    }
                }

                j++;
            }
        }
    }

    private void DrawCursor(SKCanvas canvas, Cell[,] cells, Screen screen, ModeInfo? modeInfo, TextLayoutParameters textParam)
    {
        var cursorPercentage = modeInfo is { CursorStyleEnabled: true }
            ? Math.Clamp(modeInfo.CellPercentage, 1, 100)
            : 100;
        var cursorShape = modeInfo is { CursorStyleEnabled: true }
            ? modeInfo.CursorShape
            : CursorShape.Block;
        int cellWidth = GetCharWidth(cells, screen.CursorPosition.Row, screen.CursorPosition.Col);

        float left, top, right, bottom;
        switch (cursorShape)
        {
            case CursorShape.Vertical:
                left = screen.CursorPosition.Col * textParam.CharWidth;
                top = screen.CursorPosition.Row * textParam.LineHeight;
                right = (screen.CursorPosition.Col + (cursorPercentage / 100f)) * textParam.CharWidth;
                bottom = (screen.CursorPosition.Row + 1) * textParam.LineHeight;
                break;
            case CursorShape.Horizontal:
                float topMargin = textParam.LineHeight * (100 - cursorPercentage) / 100f;
                left = screen.CursorPosition.Col * textParam.CharWidth;
                top = (screen.CursorPosition.Row * textParam.LineHeight) + topMargin;
                right = (screen.CursorPosition.Col + cellWidth) * textParam.CharWidth;
                bottom = (screen.CursorPosition.Row + 1) * textParam.LineHeight;
                break;
            default: // Block
                left = screen.CursorPosition.Col * textParam.CharWidth;
                top = screen.CursorPosition.Row * textParam.LineHeight;
                right = (screen.CursorPosition.Col + cellWidth) * textParam.CharWidth;
                bottom = (screen.CursorPosition.Row + 1) * textParam.LineHeight;
                break;
        }

        var cursorRect = new SKRect(left, top, right, bottom);
        canvas.DrawRect(cursorRect, this.cursorPaint);
    }

    private void DrawPreedit(SKCanvas canvas, Screen screen, TextLayoutParameters textParam)
    {
        string? preedit = this.imeClient.PreeditText;
        if (preedit is null)
        {
            return;
        }

        float x = screen.CursorPosition.Col * textParam.CharWidth;
        float y = screen.CursorPosition.Row * textParam.LineHeight;
        float baselineY = y + (textParam.LineHeight * 0.8f);

        float textWidth = this.textFont.MeasureText(preedit);
        this.backgroundPaint.Color = GetSkColor(screen.BackgroundColor);
        canvas.DrawRect(x, y, textWidth, textParam.LineHeight, this.backgroundPaint);

        this.textPaint.Color = GetSkColor(screen.ForegroundColor);
        canvas.DrawText(preedit, x, baselineY, SKTextAlign.Left, this.textFont, this.textPaint);

        float underlineY = y + textParam.LineHeight - 1;
        this.preeditUnderlinePaint.Color = GetSkColor(screen.ForegroundColor);
        canvas.DrawLine(x, underlineY, x + textWidth, underlineY, this.preeditUnderlinePaint);
    }

    private void DrawPlainTextRange(
        SKCanvas canvas,
        Cell[,] cells,
        int row,
        int colStart,
        int colEnd,
        SKTypeface styledTypeface,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant,
        float baselineY,
        TextLayoutParameters textParam)
    {
        bool bold = this.textFont.Embolden;
        this.plainGlyphBatch.Clear();
        SKTypeface? batchTypeface = null;

        int cellIndex = colStart;
        while (cellIndex < colEnd)
        {
            var cell = cells[row, cellIndex];
            if (cell.Character is null)
            {
                cellIndex++;
                continue;
            }

            string text = cell.Character;
            int codePoint = char.ConvertToUtf32(text, 0);
            float x = cellIndex * textParam.CharWidth;
            var typeface = this.fontChain.GetTypefaceForGlyph(codePoint, text, weight, slant);

            if (batchTypeface is not null && batchTypeface.Handle != typeface.Handle)
            {
                this.FlushPlainTextBatch(canvas, batchTypeface, bold, baselineY, textParam);
                this.plainGlyphBatch.Clear();
            }

            batchTypeface = typeface;
            this.plainGlyphBatch.Add(new PlainGlyphEntry(text, x));

            int charWidth = GetCharWidth(cells, row, cellIndex);
            cellIndex += charWidth;
        }

        if (batchTypeface is not null && this.plainGlyphBatch.Count > 0)
        {
            this.FlushPlainTextBatch(canvas, batchTypeface, bold, baselineY, textParam);
        }
    }

    private void FlushPlainTextBatch(
        SKCanvas canvas,
        SKTypeface typeface,
        bool bold,
        float baselineY,
        TextLayoutParameters textParam)
    {
        int count = this.plainGlyphBatch.Count;
        if (count == 0)
        {
            return;
        }

        this.batchTextBuilder.Clear();
        for (int i = 0; i < count; i++)
        {
            this.batchTextBuilder.Append(this.plainGlyphBatch[i].Text);
        }

        using var font = new SKFont(typeface, textParam.SkiaFontSize, 1f, 0f);
        font.Embolden = bold;

        string batchText = this.batchTextBuilder.ToString();
        int glyphCount = font.CountGlyphs(batchText);

        if (glyphCount != count)
        {
            // Glyph count mismatch (e.g. multi-glyph grapheme clusters):
            // fall back to per-cell drawing.
            this.textFont.Typeface = typeface;
            for (int i = 0; i < count; i++)
            {
                var entry = this.plainGlyphBatch[i];
                canvas.DrawText(entry.Text, entry.X, baselineY, SKTextAlign.Left, this.textFont, this.textPaint);
            }

            return;
        }

        ushort[]? glyphRented = null;
        SKPoint[]? posRented = null;
        try
        {
            Span<ushort> glyphStackBuf = stackalloc ushort[64];
            Span<SKPoint> posStackBuf = stackalloc SKPoint[64];
            Span<ushort> glyphIds = glyphCount <= 64
                ? glyphStackBuf[..glyphCount]
                : (glyphRented = ArrayPool<ushort>.Shared.Rent(glyphCount)).AsSpan(0, glyphCount);
            Span<SKPoint> positions = glyphCount <= 64
                ? posStackBuf[..glyphCount]
                : (posRented = ArrayPool<SKPoint>.Shared.Rent(glyphCount)).AsSpan(0, glyphCount);

            font.GetGlyphs(batchText, glyphIds);

            for (int i = 0; i < count; i++)
            {
                positions[i] = new SKPoint(this.plainGlyphBatch[i].X, baselineY);
            }

            using var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun(glyphIds, font, positions);
            using var blob = builder.Build();
            if (blob is not null)
            {
                canvas.DrawText(blob, 0, 0, this.textPaint);
            }
        }
        finally
        {
            if (glyphRented is not null)
            {
                ArrayPool<ushort>.Shared.Return(glyphRented);
            }

            if (posRented is not null)
            {
                ArrayPool<SKPoint>.Shared.Return(posRented);
            }
        }
    }

    private void DrawLigatureTextRange(
        SKCanvas canvas,
        Cell[,] cells,
        int row,
        int colStart,
        int colEnd,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant,
        float baselineY,
        bool embolden,
        TextLayoutParameters textParam)
    {
        foreach (var run in this.BuildResolvedTypefaceRuns(cells, row, colStart, colEnd, weight, slant))
        {
            var shapedRun = this.ligatureTextShaper.ShapeText(run.Typeface, textParam.SkiaFontSize, run.Text);
            if (shapedRun is null || !this.DrawAnchoredShapedRun(canvas, run, shapedRun, baselineY, embolden, textParam))
            {
                this.textFont.Typeface = run.Typeface;
                this.textFont.Embolden = embolden;
                this.DrawPlainTextRange(canvas, cells, row, run.StartColumn, run.EndColumn, run.Typeface, weight, slant, baselineY, textParam);
                this.textFont.Embolden = false;
            }
        }
    }

    private bool DrawAnchoredShapedRun(
        SKCanvas canvas,
        ResolvedTypefaceRun run,
        LigatureTextShaper.ShapedTextRun shapedRun,
        float baselineY,
        bool embolden,
        TextLayoutParameters textParam)
    {
        Span<ushort> glyphBuffer = stackalloc ushort[16];
        Span<SKPoint> pointBuffer = stackalloc SKPoint[16];
        var cellSpans = CollectionsMarshal.AsSpan(this.allCellSpans)
            .Slice(run.CellSpanOffset, run.CellSpanCount);
        int glyphStart = 0;
        while (glyphStart < shapedRun.GlyphCount)
        {
            uint clusterStart = shapedRun.Clusters[glyphStart];
            int glyphEnd = glyphStart + 1;
            while (glyphEnd < shapedRun.GlyphCount && shapedRun.Clusters[glyphEnd] == clusterStart)
            {
                glyphEnd++;
            }

            int clusterEnd = glyphEnd < shapedRun.GlyphCount
                ? checked((int)shapedRun.Clusters[glyphEnd])
                : run.Text.Length;
            if (!TryGetClusterStartColumn(cellSpans, checked((int)clusterStart), clusterEnd, out int startColumn))
            {
                return false;
            }

            int count = glyphEnd - glyphStart;
            ushort[]? rentedGlyphs = null;
            SKPoint[]? rentedPoints = null;
            try
            {
                Span<ushort> glyphIds = count <= 16
                    ? glyphBuffer[..count]
                    : (rentedGlyphs = ArrayPool<ushort>.Shared.Rent(count)).AsSpan(0, count);
                Span<SKPoint> points = count <= 16
                    ? pointBuffer[..count]
                    : (rentedPoints = ArrayPool<SKPoint>.Shared.Rent(count)).AsSpan(0, count);
                float clusterOriginX = shapedRun.Points[glyphStart].X;
                for (int i = 0; i < count; i++)
                {
                    glyphIds[i] = shapedRun.GlyphIds[glyphStart + i];
                    var point = shapedRun.Points[glyphStart + i];
                    points[i] = new SKPoint(point.X - clusterOriginX, point.Y);
                }

                using var blob = this.ligatureTextShaper.CreateTextBlob(run.Typeface, textParam.SkiaFontSize, glyphIds, points, embolden);
                if (blob is null)
                {
                    return false;
                }

                canvas.DrawText(blob, startColumn * textParam.CharWidth, baselineY, this.textPaint);
            }
            finally
            {
                if (rentedGlyphs is not null)
                {
                    ArrayPool<ushort>.Shared.Return(rentedGlyphs);
                }

                if (rentedPoints is not null)
                {
                    ArrayPool<SKPoint>.Shared.Return(rentedPoints);
                }
            }

            glyphStart = glyphEnd;
        }

        return true;
    }

    private List<ResolvedTypefaceRun> BuildResolvedTypefaceRuns(
        Cell[,] cells,
        int row,
        int colStart,
        int colEnd,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant)
    {
        this.resolvedRuns.Clear();
        this.allCellSpans.Clear();
        SKTypeface? currentTypeface = null;
        this.runTextBuilder.Clear();
        this.runCellSpans.Clear();
        int runStart = colStart;
        int runEnd = colStart;

        void FlushCurrentRun()
        {
            if (currentTypeface is null || this.runTextBuilder.Length == 0)
            {
                return;
            }

            int spanOffset = this.allCellSpans.Count;
            this.allCellSpans.AddRange(this.runCellSpans);
            this.resolvedRuns.Add(new ResolvedTypefaceRun(
                runStart,
                runEnd,
                this.runTextBuilder.ToString(),
                currentTypeface,
                spanOffset,
                this.runCellSpans.Count));
        }

        int cellIndex = colStart;
        while (cellIndex < colEnd)
        {
            var cell = cells[row, cellIndex];
            if (cell.Character is null)
            {
                cellIndex++;
                continue;
            }

            string text = cell.Character;
            int charWidth = GetCharWidth(cells, row, cellIndex);
            int codePoint = char.ConvertToUtf32(text, 0);
            var typeface = this.fontChain.GetTypefaceForGlyph(codePoint, text, weight, slant);

            if (currentTypeface is null || currentTypeface.Handle != typeface.Handle)
            {
                FlushCurrentRun();
                currentTypeface = typeface;
                this.runTextBuilder.Clear();
                this.runCellSpans.Clear();
                runStart = cellIndex;
            }

            int textStart = this.runTextBuilder.Length;
            this.runTextBuilder.Append(text);
            this.runCellSpans.Add(new TextCellSpan(textStart, text.Length, cellIndex, charWidth));
            runEnd = cellIndex + charWidth;
            cellIndex += charWidth;
        }

        FlushCurrentRun();
        return this.resolvedRuns;
    }

    private readonly struct ResolvedTypefaceRun
    {
        public ResolvedTypefaceRun(int startColumn, int endColumn, string text, SKTypeface typeface, int cellSpanOffset, int cellSpanCount)
        {
            this.StartColumn = startColumn;
            this.EndColumn = endColumn;
            this.Text = text;
            this.Typeface = typeface;
            this.CellSpanOffset = cellSpanOffset;
            this.CellSpanCount = cellSpanCount;
        }

        public int StartColumn { get; }

        public int EndColumn { get; }

        public string Text { get; }

        public SKTypeface Typeface { get; }

        public int CellSpanOffset { get; }

        public int CellSpanCount { get; }
    }

    private readonly struct TextCellSpan
    {
        public TextCellSpan(int textStart, int textLength, int columnStart, int columnWidth)
        {
            this.TextStart = textStart;
            this.TextLength = textLength;
            this.ColumnStart = columnStart;
            this.ColumnWidth = columnWidth;
        }

        public int TextStart { get; }

        public int TextLength { get; }

        public int ColumnStart { get; }

        public int ColumnWidth { get; }
    }

    /// <summary>
    /// Accumulates a cell's character text and x position for batched
    /// <see cref="SKTextBlob"/> drawing in the plain-text path.
    /// </summary>
    private readonly struct PlainGlyphEntry
    {
        public PlainGlyphEntry(string text, float x)
        {
            this.Text = text;
            this.X = x;
        }

        public string Text { get; }

        public float X { get; }
    }
}
