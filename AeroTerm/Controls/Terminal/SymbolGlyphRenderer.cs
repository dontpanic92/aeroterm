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
    /// <summary>
    /// Tiles are rasterized at device resolution, so the blit is very close to
    /// 1:1. Linear filtering absorbs the sub-pixel phase difference without
    /// dropping or doubling stroke rows the way nearest sampling would.
    /// </summary>
    private static readonly SKSamplingOptions AtlasSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt };
    private readonly SKPaint fillPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint aaFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath scratchPath = new();
    private readonly SKPaint atlasPaint = new() { IsAntialias = false };
    private SymbolGlyphAtlas? atlas;
    private int atlasTileWidth;
    private int atlasTileHeight;
    private bool isDisposed;

    /// <summary>
    /// Gets the number of glyphs blitted from the atlas. Exposed for tests
    /// and render diagnostics.
    /// </summary>
    internal int AtlasDrawCount { get; private set; }

    /// <summary>
    /// Gets the number of glyphs that bypassed the atlas and were painted
    /// directly from vector primitives. Exposed for tests and diagnostics.
    /// </summary>
    internal int DirectDrawCount { get; private set; }

    /// <summary>
    /// Points the renderer at the atlas matching the current cell geometry.
    /// Call once per frame before batching; rebuilding only happens when the
    /// device-pixel cell size actually changes.
    /// </summary>
    /// <param name="cellWidth">Cell width in device-independent pixels.</param>
    /// <param name="cellHeight">Cell height in device-independent pixels.</param>
    /// <param name="scaleX">Horizontal device scale.</param>
    /// <param name="scaleY">Vertical device scale.</param>
    public void SetCellGeometry(float cellWidth, float cellHeight, float scaleX, float scaleY)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);

        int tileWidth = (int)Math.Max(1, Math.Round(cellWidth * scaleX));
        int tileHeight = (int)Math.Max(1, Math.Round(cellHeight * scaleY));
        if (this.atlas is not null && tileWidth == this.atlasTileWidth && tileHeight == this.atlasTileHeight)
        {
            return;
        }

        this.atlas?.Dispose();
        this.atlas = SymbolGlyphAtlas.Acquire(tileWidth, tileHeight);
        this.atlasTileWidth = tileWidth;
        this.atlasTileHeight = tileHeight;
    }

    /// <summary>
    /// Draws one symbol cell by blitting its pre-rasterized coverage mask
    /// from the shared atlas, tinted to the cell's foreground color.
    /// </summary>
    /// <param name="canvas">The Skia canvas to paint on.</param>
    /// <param name="codePoint">The Unicode code point to render.</param>
    /// <param name="cellRect">The exact rectangle the glyph must occupy.</param>
    /// <param name="color">The foreground color to tint the mask with.</param>
    /// <returns><see langword="true"/> when the glyph was drawn from the
    /// atlas; <see langword="false"/> when the caller must fall back to
    /// <see cref="TryDraw"/> for this cell.</returns>
    public bool TryDrawFromAtlas(SKCanvas canvas, int codePoint, SKRect cellRect, SKColor color)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        ArgumentNullException.ThrowIfNull(canvas);

        if (this.atlas is null || !this.atlas.TryGetTile(codePoint, out SKRect textureRect))
        {
            return false;
        }

        this.atlasPaint.Color = color;
        canvas.DrawImage(this.atlas.Image, textureRect, cellRect, AtlasSampling, this.atlasPaint);
        this.AtlasDrawCount++;
        return true;
    }

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

        this.DirectDrawCount++;
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
        this.atlasPaint.Dispose();
        this.atlas?.Dispose();
        this.atlas = null;
        this.isDisposed = true;
    }
}
