// <copyright file="BlockElements.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Programmatic painter for the Block Elements block (U+2580..U+259F).
/// </summary>
/// <remarks>
/// Every glyph is decomposed into one or more axis-aligned filled
/// rectangles snapped to the cell rect. Shaded blocks (U+2591..U+2593)
/// are rendered as a single full-cell fill at 25%, 50% and 75% alpha so
/// adjacent shaded cells tile cleanly without seams.
/// </remarks>
internal static class BlockElements
{
    /// <summary>
    /// Paints the glyph for the given block-elements code point.
    /// </summary>
    /// <param name="canvas">Canvas to draw on.</param>
    /// <param name="codePoint">Unicode code point in U+2580..U+259F.</param>
    /// <param name="rect">Cell rect in canvas coordinates.</param>
    /// <param name="fillPaint">Pre-coloured fill paint, AA off.</param>
    public static void Draw(SKCanvas canvas, int codePoint, SKRect rect, SKPaint fillPaint)
    {
        float w = rect.Right - rect.Left;
        float h = rect.Bottom - rect.Top;

        switch (codePoint)
        {
            case 0x2580: // ▀ upper half block
                FillVerticalFraction(canvas, rect, fillPaint, fromTop: true, fraction: 0.5f);
                return;

            case 0x2581: // ▁ lower one eighth block
            case 0x2582: // ▂ lower one quarter
            case 0x2583: // ▃ lower three eighths
            case 0x2584: // ▄ lower half
            case 0x2585: // ▅ lower five eighths
            case 0x2586: // ▆ lower three quarters
            case 0x2587: // ▇ lower seven eighths
            case 0x2588: // █ full block
                {
                    int eighths = codePoint - 0x2580; // 1..8
                    FillVerticalFraction(canvas, rect, fillPaint, fromTop: false, fraction: eighths / 8f);
                }

                return;

            case 0x2589: // ▉ left seven eighths
            case 0x258A: // ▊ left three quarters
            case 0x258B: // ▋ left five eighths
            case 0x258C: // ▌ left half
            case 0x258D: // ▍ left three eighths
            case 0x258E: // ▎ left one quarter
            case 0x258F: // ▏ left one eighth
                {
                    int eighths = 8 - (codePoint - 0x2588); // 7..1
                    FillHorizontalFraction(canvas, rect, fillPaint, fromLeft: true, fraction: eighths / 8f);
                }

                return;

            case 0x2590: // ▐ right half
                FillHorizontalFraction(canvas, rect, fillPaint, fromLeft: false, fraction: 0.5f);
                return;

            case 0x2591: // ░ light shade (25%)
            case 0x2592: // ▒ medium shade (50%)
            case 0x2593: // ▓ dark shade (75%)
                FillShade(canvas, rect, fillPaint, codePoint);
                return;

            case 0x2594: // ▔ upper one eighth
                FillVerticalFraction(canvas, rect, fillPaint, fromTop: true, fraction: 1f / 8f);
                return;

            case 0x2595: // ▕ right one eighth
                FillHorizontalFraction(canvas, rect, fillPaint, fromLeft: false, fraction: 1f / 8f);
                return;

            case 0x2596: // ▖ quadrant lower left
                FillQuadrant(canvas, rect, fillPaint, ql: true, qr: false, ul: false, ur: false);
                return;
            case 0x2597: // ▗ quadrant lower right
                FillQuadrant(canvas, rect, fillPaint, ql: false, qr: true, ul: false, ur: false);
                return;
            case 0x2598: // ▘ quadrant upper left
                FillQuadrant(canvas, rect, fillPaint, ql: false, qr: false, ul: true, ur: false);
                return;
            case 0x2599: // ▙ quadrant ul + ll + lr
                FillQuadrant(canvas, rect, fillPaint, ql: true, qr: true, ul: true, ur: false);
                return;
            case 0x259A: // ▚ quadrant ul + lr
                FillQuadrant(canvas, rect, fillPaint, ql: false, qr: true, ul: true, ur: false);
                return;
            case 0x259B: // ▛ quadrant ul + ur + ll
                FillQuadrant(canvas, rect, fillPaint, ql: true, qr: false, ul: true, ur: true);
                return;
            case 0x259C: // ▜ quadrant ul + ur + lr
                FillQuadrant(canvas, rect, fillPaint, ql: false, qr: true, ul: true, ur: true);
                return;
            case 0x259D: // ▝ quadrant upper right
                FillQuadrant(canvas, rect, fillPaint, ql: false, qr: false, ul: false, ur: true);
                return;
            case 0x259E: // ▞ quadrant ur + ll
                FillQuadrant(canvas, rect, fillPaint, ql: true, qr: false, ul: false, ur: true);
                return;
            case 0x259F: // ▟ quadrant ur + ll + lr
                FillQuadrant(canvas, rect, fillPaint, ql: true, qr: true, ul: false, ur: true);
                return;
        }

        // Unreachable for in-range code points; keep the parameter live.
        _ = w;
        _ = h;
    }

    private static void FillVerticalFraction(SKCanvas canvas, SKRect rect, SKPaint fill, bool fromTop, float fraction)
    {
        float h = (rect.Bottom - rect.Top) * fraction;
        if (fromTop)
        {
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + h), fill);
        }
        else
        {
            canvas.DrawRect(new SKRect(rect.Left, rect.Bottom - h, rect.Right, rect.Bottom), fill);
        }
    }

    private static void FillHorizontalFraction(SKCanvas canvas, SKRect rect, SKPaint fill, bool fromLeft, float fraction)
    {
        float w = (rect.Right - rect.Left) * fraction;
        if (fromLeft)
        {
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + w, rect.Bottom), fill);
        }
        else
        {
            canvas.DrawRect(new SKRect(rect.Right - w, rect.Top, rect.Right, rect.Bottom), fill);
        }
    }

    private static void FillShade(SKCanvas canvas, SKRect rect, SKPaint fill, int codePoint)
    {
        // Use alpha blending to keep adjacent shaded cells seamless. The
        // reference shading levels for ░▒▓ are 25/50/75 percent.
        SKColor original = fill.Color;
        byte alpha = codePoint switch
        {
            0x2591 => 64,
            0x2592 => 128,
            0x2593 => 192,
            _ => 255,
        };
        fill.Color = original.WithAlpha((byte)((original.Alpha * alpha) / 255));
        try
        {
            canvas.DrawRect(rect, fill);
        }
        finally
        {
            fill.Color = original;
        }
    }

    private static void FillQuadrant(SKCanvas canvas, SKRect rect, SKPaint fill, bool ql, bool qr, bool ul, bool ur)
    {
        // Snap the cell midpoints to integer pixels so adjacent quadrant
        // glyphs tile without seams.
        float mx = rect.Left + (float)Math.Floor((rect.Right - rect.Left) / 2);
        float my = rect.Top + (float)Math.Floor((rect.Bottom - rect.Top) / 2);

        if (ul)
        {
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, mx, my), fill);
        }

        if (ur)
        {
            canvas.DrawRect(new SKRect(mx, rect.Top, rect.Right, my), fill);
        }

        if (ql)
        {
            canvas.DrawRect(new SKRect(rect.Left, my, mx, rect.Bottom), fill);
        }

        if (qr)
        {
            canvas.DrawRect(new SKRect(mx, my, rect.Right, rect.Bottom), fill);
        }
    }
}
