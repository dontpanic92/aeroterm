// <copyright file="SymbolGlyphRendererTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Pixel-level tests for <see cref="SymbolGlyphRenderer"/> verifying that
/// adjacent / stacked symbol cells paint with no background-coloured gap
/// between them. These would have failed before the renderer was added,
/// because the box-drawing glyphs in the user's Iosevka font draw at
/// their natural advance, which is shorter than the rounded-up cell
/// width.
/// </summary>
[TestFixture]
public class SymbolGlyphRendererTests
{
    // Cell size matches what Iosevka 14pt produces in the user's
    // session: 10x24, where the natural-advance gap reaches its worst
    // (0.67 px on the right, 0.67 px at the bottom).
    private const int CellW = 10;
    private const int CellH = 24;

    /// <summary>Horizontal lines must connect at the cell boundary.</summary>
    /// <param name="codePoint">Box-drawing horizontal line variant.</param>
    [TestCase(0x2500)] // ─
    [TestCase(0x2501)] // ━
    [TestCase(0x2550)] // ═
    public void HorizontalLineJoinsAcrossCells(int codePoint)
    {
        using var bitmap = RenderTwoCells(codePoint, stackedVertically: false);

        bool anyRowHadForeground = false;
        for (int y = 0; y < CellH; y++)
        {
            bool leftHasFg = false;
            bool rightHasFg = false;
            for (int x = 0; x < CellW; x++)
            {
                if (IsForeground(bitmap.GetPixel(x, y)))
                {
                    leftHasFg = true;
                }

                if (IsForeground(bitmap.GetPixel(CellW + x, y)))
                {
                    rightHasFg = true;
                }
            }

            if (leftHasFg && rightHasFg)
            {
                anyRowHadForeground = true;
                Assert.That(IsForeground(bitmap.GetPixel(CellW - 1, y)), Is.True, $"Gap at left cell-edge column on row {y}");
                Assert.That(IsForeground(bitmap.GetPixel(CellW, y)), Is.True, $"Gap at right cell-edge column on row {y}");
            }
        }

        Assert.That(anyRowHadForeground, Is.True, "No foreground pixels found at all — render produced nothing.");
    }

    /// <summary>Vertical lines must connect at the cell boundary.</summary>
    /// <param name="codePoint">Box-drawing vertical line variant.</param>
    [TestCase(0x2502)] // │
    [TestCase(0x2503)] // ┃
    [TestCase(0x2551)] // ║
    public void VerticalLineJoinsAcrossCells(int codePoint)
    {
        using var bitmap = RenderTwoCells(codePoint, stackedVertically: true);

        bool anyColHadForeground = false;
        for (int x = 0; x < CellW; x++)
        {
            bool topHasFg = false;
            bool botHasFg = false;
            for (int y = 0; y < CellH; y++)
            {
                if (IsForeground(bitmap.GetPixel(x, y)))
                {
                    topHasFg = true;
                }

                if (IsForeground(bitmap.GetPixel(x, CellH + y)))
                {
                    botHasFg = true;
                }
            }

            if (topHasFg && botHasFg)
            {
                anyColHadForeground = true;
                Assert.That(IsForeground(bitmap.GetPixel(x, CellH - 1)), Is.True, $"Gap at bottom cell-edge row on col {x}");
                Assert.That(IsForeground(bitmap.GetPixel(x, CellH)), Is.True, $"Gap at top cell-edge row on col {x}");
            }
        }

        Assert.That(anyColHadForeground, Is.True, "No foreground pixels found at all — render produced nothing.");
    }

    /// <summary>U+2588 full-block tiles seamlessly across cells.</summary>
    [Test]
    public void FullBlockTilesWithoutGap()
    {
        using var bitmap = RenderTwoCells(0x2588, stackedVertically: false);
        for (int y = 0; y < CellH; y++)
        {
            for (int x = 0; x < CellW * 2; x++)
            {
                Assert.That(IsForeground(bitmap.GetPixel(x, y)), Is.True, $"Full block missing pixel at ({x},{y})");
            }
        }
    }

    /// <summary>Rounded-corner glyphs connect to their adjacent horizontal stroke without a gap.</summary>
    /// <param name="leftCp">Code point in the left cell.</param>
    /// <param name="rightCp">Code point in the right cell.</param>
    [TestCase(0x256D, 0x2500)] // ╭─
    [TestCase(0x2500, 0x256E)] // ─╮
    [TestCase(0x2570, 0x2500)] // ╰─
    [TestCase(0x2500, 0x256F)] // ─╯
    public void RoundedCornersConnectToHorizontal(int leftCp, int rightCp)
    {
        using var bitmap = new SKBitmap(CellW * 2, CellH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var renderer = new SymbolGlyphRenderer();

        Assume.That(renderer.TryDraw(canvas, leftCp, new SKRect(0, 0, CellW, CellH), SKColors.White), Is.True);
        Assume.That(renderer.TryDraw(canvas, rightCp, new SKRect(CellW, 0, CellW * 2, CellH), SKColors.White), Is.True);
        canvas.Flush();

        bool foundConnectedRow = false;
        for (int y = CellH / 3; y < (2 * CellH) / 3; y++)
        {
            bool leftFg = IsForeground(bitmap.GetPixel(CellW - 1, y));
            bool rightFg = IsForeground(bitmap.GetPixel(CellW, y));
            if (leftFg && rightFg)
            {
                foundConnectedRow = true;
                break;
            }
        }

        Assert.That(foundConnectedRow, Is.True, "Rounded corner does not connect to the adjacent horizontal stroke.");
    }

    /// <summary>U+2550 (═) is rendered as two distinct parallel strokes.</summary>
    [Test]
    public void DoubleHorizontalLineHasTwoDistinctStrokes()
    {
        using var bitmap = RenderTwoCells(0x2550, stackedVertically: false);

        int transitions = 0;
        bool prevFg = false;
        for (int y = 0; y < CellH; y++)
        {
            bool fg = IsForeground(bitmap.GetPixel(CellW / 2, y));
            if (fg != prevFg)
            {
                transitions++;
                prevFg = fg;
            }
        }

        Assert.That(transitions, Is.EqualTo(4), "Double line should have exactly two foreground bands.");
    }

    /// <summary>Sextant rendering covers Symbols for Legacy Computing.</summary>
    [Test]
    public void SextantRendersFromLegacyComputingRange()
    {
        // Walk the supported sextant range; for every code point the
        // renderer must accept it and produce *some* foreground pixel.
        using var renderer = new SymbolGlyphRenderer();
        for (int cp = 0x1FB00; cp <= 0x1FB3B; cp++)
        {
            using var bitmap = new SKBitmap(CellW, CellH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            Assert.That(renderer.TryDraw(canvas, cp, new SKRect(0, 0, CellW, CellH), SKColors.White), Is.True, $"U+{cp:X4} not handled");
            canvas.Flush();

            bool hasFg = false;
            for (int y = 0; y < CellH && !hasFg; y++)
            {
                for (int x = 0; x < CellW && !hasFg; x++)
                {
                    if (IsForeground(bitmap.GetPixel(x, y)))
                    {
                        hasFg = true;
                    }
                }
            }

            Assert.That(hasFg, Is.True, $"Sextant U+{cp:X4} produced no foreground pixels");
        }
    }

    /// <summary>Verifies that representative Braille code points are accepted and produce the expected number of dots.</summary>
    /// <param name="codePoint">A Braille pattern code point.</param>
    /// <param name="expectedDots">The expected number of set dots (popcount of the low 8 bits).</param>
    [TestCase(0x2800, 0)] // BRAILLE PATTERN BLANK
    [TestCase(0x2801, 1)] // dot 1 only
    [TestCase(0x2880, 1)] // dot 8 only
    [TestCase(0x28FF, 8)] // all 8 dots
    [TestCase(0x2847, 4)] // dots 1, 2, 3, 7 (lower-left corner)
    public void BrailleRendersExpectedDotCount(int codePoint, int expectedDots)
    {
        // Use a slightly larger cell so individual dots are easily resolved.
        const int W = 12;
        const int H = 28;
        using var bitmap = new SKBitmap(W, H, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var renderer = new SymbolGlyphRenderer();
        Assume.That(renderer.TryDraw(canvas, codePoint, new SKRect(0, 0, W, H), SKColors.White), Is.True);
        canvas.Flush();

        // Sample the 8 documented dot positions and count those that
        // landed on a foreground pixel.
        int[] dotMaskByPosition = { 0x01, 0x02, 0x04, 0x40, 0x08, 0x10, 0x20, 0x80 };
        var positions = new (float X, float Y)[]
        {
            (W * 0.25f, H * 0.125f),
            (W * 0.25f, H * 0.375f),
            (W * 0.25f, H * 0.625f),
            (W * 0.25f, H * 0.875f),
            (W * 0.75f, H * 0.125f),
            (W * 0.75f, H * 0.375f),
            (W * 0.75f, H * 0.625f),
            (W * 0.75f, H * 0.875f),
        };

        int actualDots = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            int sx = (int)positions[i].X;
            int sy = (int)positions[i].Y;
            bool hasDot = IsForeground(bitmap.GetPixel(sx, sy));
            if (hasDot)
            {
                actualDots++;
            }

            bool expectSet = (codePoint & dotMaskByPosition[i]) != 0;
            Assert.That(hasDot, Is.EqualTo(expectSet), $"Dot position {i} (mask 0x{dotMaskByPosition[i]:X2}) of U+{codePoint:X4}");
        }

        Assert.That(actualDots, Is.EqualTo(expectedDots));
    }

    /// <summary><see cref="SymbolGlyphRanges.Handles"/> covers the documented ranges and excludes neighbours.</summary>
    [Test]
    public void SymbolGlyphRangesHandlesExpectedRanges()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SymbolGlyphRanges.Handles(0x2500), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x257F), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x2580), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x259F), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x2800), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x28FF), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0xE0A0), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0xE0D4), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x1FB00), Is.True);
            Assert.That(SymbolGlyphRanges.Handles(0x1FBFF), Is.True);

            Assert.That(SymbolGlyphRanges.Handles('A'), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x24FF), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x25A0), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x27FF), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x2900), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0xE09F), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0xE0D5), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x1FAFF), Is.False);
            Assert.That(SymbolGlyphRanges.Handles(0x1FC00), Is.False);
        });
    }

    private static SKBitmap RenderTwoCells(int codePoint, bool stackedVertically)
    {
        int width = stackedVertically ? CellW : CellW * 2;
        int height = stackedVertically ? CellH * 2 : CellH;

        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        using var renderer = new SymbolGlyphRenderer();
        var c1 = new SKRect(0, 0, CellW, CellH);
        var c2 = stackedVertically
            ? new SKRect(0, CellH, CellW, CellH * 2)
            : new SKRect(CellW, 0, CellW * 2, CellH);

        Assume.That(renderer.TryDraw(canvas, codePoint, c1, SKColors.White), Is.True);
        Assume.That(renderer.TryDraw(canvas, codePoint, c2, SKColors.White), Is.True);
        canvas.Flush();
        return bitmap;
    }

    private static bool IsForeground(SKColor c)
    {
        // Treat any pixel with non-trivial luma as foreground; AA arcs
        // may leave intermediate values, so use 32/255 as the threshold.
        int luma = (c.Red + c.Green + c.Blue) / 3;
        return luma >= 32;
    }
}
