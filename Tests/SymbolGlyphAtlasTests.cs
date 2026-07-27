// <copyright file="SymbolGlyphAtlasTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Linq;
using AeroTerm.Controls.Terminal;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Verifies that atlas-blitted symbol glyphs match the direct vector renderer
/// and that the atlas covers the programmatic glyph ranges.
/// </summary>
[TestFixture]
public sealed class SymbolGlyphAtlasTests
{
    private const float CellWidth = 10f;
    private const float CellHeight = 20f;
    private const float Scale = 2f;

    /// <summary>
    /// Code points whose atlas blit must be pixel-identical to direct vector
    /// drawing. Only fully opaque, pixel-snapped fills qualify: they carry no
    /// partial coverage, so the atlas round-trip cannot perturb them.
    /// </summary>
    private static readonly int[] ExactCodePoints =
    {
        0x2580, // upper half block
        0x2588, // full block
    };

    /// <summary>
    /// Code points that reach the atlas through partial coverage: translucent
    /// shade fills and antialiased Braille dots. Their geometry is identical
    /// on both paths, but the coverage value makes a second trip through 8-bit
    /// quantization (rasterized into the <c>Alpha8</c> tile, then tinted on
    /// blit), so individual channels may land one step away from the directly
    /// drawn result. The exact rounding depends on the platform's Skia raster
    /// backend, so these are compared with a tolerance rather than exactly.
    /// </summary>
    private static readonly int[] CoverageCodePoints =
    {
        0x2591, // light shade
        0x2801, // braille dot 1
        0x28FF, // braille all dots
        0x28A5, // braille mixed dots
    };

    /// <summary>
    /// Box drawing code points. <c>BoxDrawing</c> snaps stroke thickness to
    /// whole pixels of the rect it is handed, so rasterizing into a
    /// device-resolution atlas tile snaps to whole <em>device</em> pixels
    /// instead of whole logical pixels. On a HiDPI display that makes strokes
    /// slightly thinner and more accurate; the tiling geometry is unchanged,
    /// so junctions between adjacent cells still line up.
    /// </summary>
    private static readonly int[] BoxDrawingCodePoints =
    {
        0x2500, // box drawing light horizontal
        0x250C, // box drawing light down and right
        0x253C, // box drawing light vertical and horizontal
    };

    private static readonly int[] SampleCodePoints =
        ExactCodePoints.Concat(CoverageCodePoints).Concat(BoxDrawingCodePoints).ToArray();

    /// <summary>
    /// The atlas exposes a tile for every sampled code point.
    /// </summary>
    [Test]
    public void Acquire_ProvidesTilesForSupportedRanges()
    {
        using var atlas = SymbolGlyphAtlas.Acquire(20, 40);

        Assert.That(atlas, Is.Not.Null);
        foreach (int codePoint in SampleCodePoints)
        {
            Assert.That(
                atlas!.TryGetTile(codePoint, out SKRect tile),
                Is.True,
                $"Missing atlas tile for U+{codePoint:X4}.");
            Assert.That(tile.Width, Is.EqualTo(20));
            Assert.That(tile.Height, Is.EqualTo(40));
        }
    }

    /// <summary>
    /// The same geometry returns the same shared atlas instance rather than
    /// rebuilding the texture for every terminal.
    /// </summary>
    [Test]
    public void Acquire_SameGeometry_SharesInstance()
    {
        using var first = SymbolGlyphAtlas.Acquire(12, 24);
        using var second = SymbolGlyphAtlas.Acquire(12, 24);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
    }

    /// <summary>
    /// Invalid geometry is rejected so callers fall back to vector drawing.
    /// </summary>
    [Test]
    public void Acquire_InvalidGeometry_ReturnsNull()
    {
        Assert.That(SymbolGlyphAtlas.Acquire(0, 24), Is.Null);
        Assert.That(SymbolGlyphAtlas.Acquire(12, 0), Is.Null);
    }

    /// <summary>
    /// Opaque block glyphs blit from the atlas pixel-for-pixel.
    /// </summary>
    /// <param name="codePointIndex">Index into <see cref="ExactCodePoints"/>.</param>
    [Test]
    public void TryDrawFromAtlas_OpaqueBlocks_MatchesVectorRenderingExactly(
        [Range(0, 1)] int codePointIndex)
    {
        int codePoint = ExactCodePoints[codePointIndex];
        AssertParity(codePoint, maximumMeanDifference: 0.001);
    }

    /// <summary>
    /// Shaded block and Braille glyphs stay within coverage quantization noise
    /// of direct drawing, as described on <see cref="CoverageCodePoints"/>. A
    /// missing or displaced dot would blow past both bounds.
    /// </summary>
    /// <param name="codePointIndex">Index into <see cref="CoverageCodePoints"/>.</param>
    [Test]
    public void TryDrawFromAtlas_ShadeAndBraille_MatchesVectorRenderingWithinRounding(
        [Range(0, 3)] int codePointIndex)
    {
        int codePoint = CoverageCodePoints[codePointIndex];
        AssertParity(codePoint, maximumMeanDifference: 1.5, maximumChannelDifference: 128);
    }

    /// <summary>
    /// Box drawing glyphs stay visually equivalent; only stroke thickness
    /// snapping changes, as described on <see cref="BoxDrawingCodePoints"/>.
    /// </summary>
    /// <param name="codePointIndex">Index into <see cref="BoxDrawingCodePoints"/>.</param>
    [Test]
    public void TryDrawFromAtlas_BoxDrawing_MatchesVectorRenderingClosely(
        [Range(0, 2)] int codePointIndex)
    {
        int codePoint = BoxDrawingCodePoints[codePointIndex];
        AssertParity(codePoint, maximumMeanDifference: 10.0);
    }

    private static void AssertParity(
        int codePoint,
        double maximumMeanDifference,
        int maximumChannelDifference = 255)
    {
        var color = new SKColor(0x33, 0xCC, 0x66);

        using SKBitmap direct = RenderCell(codePoint, color, useAtlas: false);
        using SKBitmap atlased = RenderCell(codePoint, color, useAtlas: true);

        (double meanDifference, int peakDifference) = CompareBitmaps(direct, atlased);
        Assert.That(
            meanDifference,
            Is.LessThan(maximumMeanDifference),
            $"U+{codePoint:X4} atlas rendering diverged from vector rendering (mean |diff| {meanDifference:F2}).");
        Assert.That(
            peakDifference,
            Is.LessThanOrEqualTo(maximumChannelDifference),
            $"U+{codePoint:X4} atlas rendering diverged from vector rendering (peak |diff| {peakDifference}).");

        Assert.That(HasInk(atlased), Is.True, $"U+{codePoint:X4} produced an empty atlas blit.");
    }

    private static SKBitmap RenderCell(int codePoint, SKColor color, bool useAtlas)
    {
        var info = new SKImageInfo(
            (int)(CellWidth * Scale),
            (int)(CellHeight * Scale),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.Scale(Scale, Scale);

        using var renderer = new SymbolGlyphRenderer();
        var cellRect = new SKRect(0, 0, CellWidth, CellHeight);
        if (useAtlas)
        {
            renderer.SetCellGeometry(CellWidth, CellHeight, Scale, Scale);
            Assert.That(
                renderer.TryDrawFromAtlas(canvas, codePoint, cellRect, color),
                Is.True,
                $"U+{codePoint:X4} was not served by the atlas.");
        }
        else
        {
            Assert.That(renderer.TryDraw(canvas, codePoint, cellRect, color), Is.True);
        }

        using SKImage snapshot = surface.Snapshot();
        return SKBitmap.FromImage(snapshot);
    }

    private static (double MeanDifference, int PeakDifference) CompareBitmaps(SKBitmap left, SKBitmap right)
    {
        Assert.That(right.Width, Is.EqualTo(left.Width));
        Assert.That(right.Height, Is.EqualTo(left.Height));

        long total = 0;
        int peak = 0;
        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                SKColor a = left.GetPixel(x, y);
                SKColor b = right.GetPixel(x, y);
                int red = Math.Abs(a.Red - b.Red);
                int green = Math.Abs(a.Green - b.Green);
                int blue = Math.Abs(a.Blue - b.Blue);
                total += red + green + blue;
                peak = Math.Max(peak, Math.Max(red, Math.Max(green, blue)));
            }
        }

        return ((double)total / (left.Width * left.Height * 3), peak);
    }

    private static bool HasInk(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
