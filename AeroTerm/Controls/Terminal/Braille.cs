// <copyright file="Braille.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Programmatic painter for Braille Patterns (U+2800..U+28FF).
/// </summary>
/// <remarks>
/// Each Braille code point encodes an 8-dot grid (2 columns × 4 rows) in
/// the low 8 bits of the code point. The bit-to-position mapping defined
/// by Unicode is:
/// <code>
///   bit 0 (0x01) = col 0, row 0   bit 3 (0x08) = col 1, row 0
///   bit 1 (0x02) = col 0, row 1   bit 4 (0x10) = col 1, row 1
///   bit 2 (0x04) = col 0, row 2   bit 5 (0x20) = col 1, row 2
///   bit 6 (0x40) = col 0, row 3   bit 7 (0x80) = col 1, row 3
/// </code>
/// TUI applications such as <c>btop</c>, <c>blessed-contrib</c> and
/// sparkline-style status renderers use Braille as a 2×4 sub-pixel canvas
/// to draw smooth charts. Painting the dots ourselves with consistent
/// geometry guarantees adjacent Braille cells tile cleanly across cell
/// boundaries regardless of the underlying font.
/// </remarks>
internal static class Braille
{
    /// <summary>
    /// Paints the Braille glyph for the given code point.
    /// </summary>
    /// <param name="canvas">Canvas to draw on.</param>
    /// <param name="codePoint">Unicode code point in U+2800..U+28FF.</param>
    /// <param name="rect">Cell rect in canvas coordinates.</param>
    /// <param name="aaFillPaint">Pre-coloured fill paint, AA on (for round dots).</param>
    public static void Draw(SKCanvas canvas, int codePoint, SKRect rect, SKPaint aaFillPaint)
    {
        int dots = codePoint & 0xFF;
        if (dots == 0)
        {
            // U+2800 BRAILLE PATTERN BLANK — no dots; render nothing.
            return;
        }

        float w = rect.Right - rect.Left;
        float h = rect.Bottom - rect.Top;

        // The 2×4 sub-cell grid. Sub-cell centers are at the centers of
        // each of the 8 partitions; this gives even spacing in both axes
        // and matches how most fonts position the dots.
        float colSpacing = w / 4f;        // col centers at w/4 and 3w/4
        float rowSpacing = h / 8f;        // row centers at h/8, 3h/8, 5h/8, 7h/8

        float col0X = rect.Left + colSpacing;
        float col1X = rect.Left + (3f * colSpacing);
        float row0Y = rect.Top + rowSpacing;
        float row1Y = rect.Top + (3f * rowSpacing);
        float row2Y = rect.Top + (5f * rowSpacing);
        float row3Y = rect.Top + (7f * rowSpacing);

        // Dot radius: large enough to be clearly visible but small enough
        // to leave space between adjacent dots. ~40% of the smaller
        // sub-cell axis works well at typical terminal cell sizes and
        // matches the visual weight of font-rendered Braille dots.
        float dotRadius = Math.Max(1f, Math.Min(colSpacing, rowSpacing * 2f) * 0.4f);

        if ((dots & 0x01) != 0)
        {
            canvas.DrawCircle(col0X, row0Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x02) != 0)
        {
            canvas.DrawCircle(col0X, row1Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x04) != 0)
        {
            canvas.DrawCircle(col0X, row2Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x40) != 0)
        {
            canvas.DrawCircle(col0X, row3Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x08) != 0)
        {
            canvas.DrawCircle(col1X, row0Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x10) != 0)
        {
            canvas.DrawCircle(col1X, row1Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x20) != 0)
        {
            canvas.DrawCircle(col1X, row2Y, dotRadius, aaFillPaint);
        }

        if ((dots & 0x80) != 0)
        {
            canvas.DrawCircle(col1X, row3Y, dotRadius, aaFillPaint);
        }
    }
}
