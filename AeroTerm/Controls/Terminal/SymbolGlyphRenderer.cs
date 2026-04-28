// <copyright file="SymbolGlyphRenderer.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Paints terminal symbol glyphs (box drawing, block elements, Powerline,
/// Symbols for Legacy Computing) using Skia primitives sized to the exact
/// cell rect, bypassing the font path.
/// </summary>
/// <remarks>
/// See <see cref="SymbolGlyphRanges"/> for the rationale and the supported
/// code-point ranges. Each entry point assumes the caller has already
/// validated that the code point is in range via
/// <see cref="SymbolGlyphRanges.Handles"/>.
/// </remarks>
internal sealed class SymbolGlyphRenderer : IDisposable
{
    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt };
    private readonly SKPaint fillPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint aaFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath scratchPath = new();
    private bool isDisposed;

    /// <summary>
    /// Draws the glyph for the given code point inside the cell rect.
    /// </summary>
    /// <param name="canvas">The Skia canvas to paint on.</param>
    /// <param name="codePoint">The Unicode code point to render. Must be in
    /// a range accepted by <see cref="SymbolGlyphRanges.Handles"/>.</param>
    /// <param name="cellRect">The exact rectangle that the glyph must
    /// occupy. Coordinates are in canvas pixels.</param>
    /// <param name="color">The foreground color to paint with.</param>
    /// <returns><c>true</c> if the glyph was painted; <c>false</c> if the
    /// code point is in range but no implementation is provided yet (the
    /// caller should fall back to font rendering for that cell).</returns>
    public bool TryDraw(SKCanvas canvas, int codePoint, SKRect cellRect, SKColor color)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);

        this.strokePaint.Color = color;
        this.fillPaint.Color = color;
        this.aaFillPaint.Color = color;

        if (codePoint >= 0x2500 && codePoint <= 0x257F)
        {
            BoxDrawing.Draw(canvas, codePoint, cellRect, this.fillPaint, this.strokePaint, this.scratchPath);
            return true;
        }

        if (codePoint >= 0x2580 && codePoint <= 0x259F)
        {
            BlockElements.Draw(canvas, codePoint, cellRect, this.fillPaint);
            return true;
        }

        if (codePoint >= 0x2800 && codePoint <= 0x28FF)
        {
            Braille.Draw(canvas, codePoint, cellRect, this.aaFillPaint);
            return true;
        }

        if (codePoint >= 0xE0A0 && codePoint <= 0xE0D4)
        {
            return Powerline.TryDraw(canvas, codePoint, cellRect, this.fillPaint, this.aaFillPaint, this.strokePaint, this.scratchPath);
        }

        if (codePoint >= 0x1FB00 && codePoint <= 0x1FBFF)
        {
            return LegacyComputing.TryDraw(canvas, codePoint, cellRect, this.fillPaint, this.aaFillPaint);
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.strokePaint.Dispose();
        this.fillPaint.Dispose();
        this.aaFillPaint.Dispose();
        this.scratchPath.Dispose();
        this.isDisposed = true;
    }
}
