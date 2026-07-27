// <copyright file="SymbolGlyphAtlas.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// A device-resolution texture atlas holding every programmatically drawn
/// terminal symbol glyph (box drawing, block elements, Braille, Powerline and
/// Symbols for Legacy Computing) rasterized once at the current cell size.
/// </summary>
/// <remarks>
/// <para>
/// Without an atlas the terminal repaints each symbol cell from vector
/// primitives on every frame. A single Braille cell costs up to eight
/// antialiased circle fills, so a full-screen Braille TUI such as
/// <c>btop</c> submits tens of thousands of antialiased coverage draws per
/// frame. Rasterizing each glyph once and blitting the tiles lets the whole
/// screen's symbols be drawn as textured quads sampled from one shared
/// texture, which Skia coalesces into a single batched draw the same way it
/// batches font glyph blits.
/// </para>
/// <para>
/// Tiles are stored as an <see cref="SKColorType.Alpha8"/> coverage mask.
/// Skia tints alpha-only images with the paint color, so one atlas serves
/// every foreground color without breaking batching.
/// </para>
/// <para>
/// Instances are immutable once constructed and are shared between terminals
/// with the same cell geometry through <see cref="Acquire"/>.
/// </para>
/// </remarks>
internal sealed class SymbolGlyphAtlas : IDisposable
{
    /// <summary>Tiles per atlas row.</summary>
    private const int Columns = 16;

    /// <summary>Upper bound on the atlas edge length in pixels.</summary>
    private const int MaxDimension = 8192;

    /// <summary>Maximum number of distinct geometries retained.</summary>
    private const int MaxCachedGeometries = 4;

    private static readonly List<SymbolGlyphAtlas> Cache = new();

    private readonly Dictionary<int, SKRect> tiles;
    private readonly SKImage image;
    private int referenceCount;

    private SymbolGlyphAtlas(int tileWidth, int tileHeight, Dictionary<int, SKRect> tiles, SKImage image)
    {
        this.TileWidth = tileWidth;
        this.TileHeight = tileHeight;
        this.tiles = tiles;
        this.image = image;
    }

    /// <summary>Gets the tile width in physical pixels.</summary>
    public int TileWidth { get; }

    /// <summary>Gets the tile height in physical pixels.</summary>
    public int TileHeight { get; }

    /// <summary>Gets the backing atlas texture.</summary>
    public SKImage Image => this.image;

    /// <summary>
    /// Returns a shared atlas for the supplied tile geometry, building one on
    /// first use. Returns <see langword="null"/> when the geometry is unusable
    /// or rasterization fails, in which case callers must fall back to direct
    /// vector drawing.
    /// </summary>
    /// <param name="tileWidth">Tile width in physical pixels.</param>
    /// <param name="tileHeight">Tile height in physical pixels.</param>
    /// <returns>A shared atlas, or <see langword="null"/>.</returns>
    public static SymbolGlyphAtlas? Acquire(int tileWidth, int tileHeight)
    {
        if (tileWidth <= 0 || tileHeight <= 0
            || tileWidth > MaxDimension / Columns
            || tileHeight > MaxDimension)
        {
            return null;
        }

        lock (Cache)
        {
            for (int i = 0; i < Cache.Count; i++)
            {
                SymbolGlyphAtlas cached = Cache[i];
                if (cached.TileWidth == tileWidth && cached.TileHeight == tileHeight)
                {
                    cached.referenceCount++;
                    return cached;
                }
            }

            SymbolGlyphAtlas? built = Build(tileWidth, tileHeight);
            if (built is null)
            {
                return null;
            }

            built.referenceCount = 1;
            Cache.Add(built);
            while (Cache.Count > MaxCachedGeometries)
            {
                int victim = -1;
                for (int i = 0; i < Cache.Count; i++)
                {
                    if (Cache[i].referenceCount <= 0)
                    {
                        victim = i;
                        break;
                    }
                }

                if (victim < 0)
                {
                    break;
                }

                Cache[victim].image.Dispose();
                Cache.RemoveAt(victim);
            }

            return built;
        }
    }

    /// <summary>
    /// Looks up the texture rectangle for a code point.
    /// </summary>
    /// <param name="codePoint">The code point to look up.</param>
    /// <param name="textureRect">The tile rectangle within <see cref="Image"/>.</param>
    /// <returns><see langword="true"/> when the atlas contains the glyph.</returns>
    public bool TryGetTile(int codePoint, out SKRect textureRect) =>
        this.tiles.TryGetValue(codePoint, out textureRect);

    /// <summary>
    /// Releases this consumer's reference. The underlying texture stays alive
    /// while other terminals still use the same geometry.
    /// </summary>
    public void Dispose()
    {
        lock (Cache)
        {
            if (this.referenceCount > 0)
            {
                this.referenceCount--;
            }
        }
    }

    private static SymbolGlyphAtlas? Build(int tileWidth, int tileHeight)
    {
        List<int> codePoints = EnumerateCandidates();
        int rows = (codePoints.Count + Columns - 1) / Columns;
        int width = Columns * tileWidth;
        int height = rows * tileHeight;
        if (height > MaxDimension)
        {
            return null;
        }

        var info = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var renderer = new SymbolGlyphRenderer();
        var tiles = new Dictionary<int, SKRect>(codePoints.Count);
        int slot = 0;
        var tileRect = new SKRect(0, 0, tileWidth, tileHeight);

        foreach (int codePoint in codePoints)
        {
            int column = slot % Columns;
            int row = slot / Columns;
            float originX = column * tileWidth;
            float originY = row * tileHeight;

            canvas.Save();
            canvas.Translate(originX, originY);
            canvas.ClipRect(tileRect);
            bool drawn = renderer.TryDraw(canvas, codePoint, tileRect, SKColors.White);
            canvas.Restore();

            if (!drawn)
            {
                // No programmatic implementation; the caller falls back to the
                // font path for this code point, so no tile is consumed.
                continue;
            }

            tiles[codePoint] = new SKRect(originX, originY, originX + tileWidth, originY + tileHeight);
            slot++;
        }

        if (tiles.Count == 0)
        {
            return null;
        }

        SKImage image = surface.Snapshot();
        return new SymbolGlyphAtlas(tileWidth, tileHeight, tiles, image);
    }

    private static List<int> EnumerateCandidates()
    {
        var codePoints = new List<int>(768);

        // U+2800 BRAILLE PATTERN BLANK draws nothing, so it is intentionally
        // excluded — the renderer reports it as drawn but leaves an empty tile.
        AddRange(codePoints, 0x2500, 0x259F);
        AddRange(codePoints, 0x2801, 0x28FF);
        AddRange(codePoints, 0xE0A0, 0xE0D4);
        AddRange(codePoints, 0x1FB00, 0x1FBFF);
        return codePoints;

        static void AddRange(List<int> target, int first, int last)
        {
            for (int codePoint = first; codePoint <= last; codePoint++)
            {
                target.Add(codePoint);
            }
        }
    }
}
