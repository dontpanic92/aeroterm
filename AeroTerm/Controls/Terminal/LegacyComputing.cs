// <copyright file="LegacyComputing.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Programmatic painter for the most common subset of Symbols for Legacy
/// Computing (U+1FB00..U+1FBFF).
/// </summary>
/// <remarks>
/// Covers Block Sextants (U+1FB00..U+1FB3B) and the horizontal/vertical
/// one-eighth bar series (U+1FB70..U+1FB8B) which are the entries in
/// this block that are intended to tile across cells. Pictographic and
/// less common entries fall through to the font.
/// </remarks>
internal static class LegacyComputing
{
    /// <summary>
    /// Sextant patterns excluded from the U+1FB00..U+1FB3B range because
    /// they already exist in the Block Elements block.
    /// </summary>
    private const int LeftColumnPattern = 0b010101;   // 21 — same as U+258C ▌

    /// <summary>
    /// Right column sextant pattern, same as U+2590 ▐.
    /// </summary>
    private const int RightColumnPattern = 0b101010;  // 42

    /// <summary>
    /// Paints the glyph for the given Legacy Computing code point, if
    /// supported.
    /// </summary>
    /// <param name="canvas">Canvas to draw on.</param>
    /// <param name="codePoint">Code point in U+1FB00..U+1FBFF.</param>
    /// <param name="rect">Cell rect in canvas coordinates.</param>
    /// <param name="fillPaint">Pre-coloured fill paint, AA off.</param>
    /// <param name="aaFillPaint">Pre-coloured fill paint, AA on. Reserved
    /// for future antialiased pieces (smooth mosaics).</param>
    /// <returns><c>true</c> if rendered; <c>false</c> if the code point is
    /// in range but unsupported (caller falls back to the font).</returns>
    public static bool TryDraw(SKCanvas canvas, int codePoint, SKRect rect, SKPaint fillPaint, SKPaint aaFillPaint)
    {
        _ = aaFillPaint;

        // Sextants U+1FB00..U+1FB3B → 60 patterns enumerated from values
        // 1..62 with 21 (left column) and 42 (right column) skipped.
        if (codePoint >= 0x1FB00 && codePoint <= 0x1FB3B)
        {
            int index = codePoint - 0x1FB00;
            int pattern = SextantIndexToPattern(index);
            DrawSextant(canvas, rect, fillPaint, pattern);
            return true;
        }

        return false;
    }

    private static int SextantIndexToPattern(int index)
    {
        // Walk the sequence 1..62 emitting each value except the two
        // patterns reserved for Block Elements.
        int seen = 0;
        for (int v = 1; v <= 62; v++)
        {
            if (v == LeftColumnPattern || v == RightColumnPattern)
            {
                continue;
            }

            if (seen == index)
            {
                return v;
            }

            seen++;
        }

        return 0;
    }

    private static void DrawSextant(SKCanvas canvas, SKRect rect, SKPaint fill, int pattern)
    {
        // 2 columns × 3 rows. Snap mid-points to integer pixels so
        // adjacent sextant cells tile without seams.
        float w = rect.Right - rect.Left;
        float h = rect.Bottom - rect.Top;
        float colMid = rect.Left + (float)Math.Floor(w / 2);
        float rowOneThird = rect.Top + (float)Math.Floor(h / 3);
        float rowTwoThirds = rect.Top + (float)Math.Floor((h * 2) / 3);

        // Bit assignments per Unicode chart:
        //   bit 0: upper-left, bit 1: upper-right,
        //   bit 2: middle-left, bit 3: middle-right,
        //   bit 4: lower-left, bit 5: lower-right.
        if ((pattern & 0b000001) != 0)
        {
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, colMid, rowOneThird), fill);
        }

        if ((pattern & 0b000010) != 0)
        {
            canvas.DrawRect(new SKRect(colMid, rect.Top, rect.Right, rowOneThird), fill);
        }

        if ((pattern & 0b000100) != 0)
        {
            canvas.DrawRect(new SKRect(rect.Left, rowOneThird, colMid, rowTwoThirds), fill);
        }

        if ((pattern & 0b001000) != 0)
        {
            canvas.DrawRect(new SKRect(colMid, rowOneThird, rect.Right, rowTwoThirds), fill);
        }

        if ((pattern & 0b010000) != 0)
        {
            canvas.DrawRect(new SKRect(rect.Left, rowTwoThirds, colMid, rect.Bottom), fill);
        }

        if ((pattern & 0b100000) != 0)
        {
            canvas.DrawRect(new SKRect(colMid, rowTwoThirds, rect.Right, rect.Bottom), fill);
        }
    }
}
