// <copyright file="Powerline.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Programmatic painter for the most common Powerline glyphs in the Nerd
/// Font private-use range (U+E0A0..U+E0D4).
/// </summary>
/// <remarks>
/// Powerline separators rely on filled triangles or rounded shapes that
/// span the entire cell so consecutive separator cells form a continuous
/// chain. The font glyphs themselves do this correctly, but the
/// sub-pixel cell-rounding gap that motivates the rest of this renderer
/// also breaks Powerline chains. Only the symbols that actually need to
/// span the cell edge are reimplemented here; pictographic glyphs
/// (branch, lock, line-number, etc.) fall through to the font.
/// </remarks>
internal static class Powerline
{
    /// <summary>
    /// Paints the glyph for the given Powerline code point if it is
    /// supported.
    /// </summary>
    /// <param name="canvas">Canvas to draw on.</param>
    /// <param name="codePoint">Unicode code point in U+E0A0..U+E0D4.</param>
    /// <param name="rect">Cell rect in canvas coordinates.</param>
    /// <param name="fillPaint">Pre-coloured fill paint, AA off.</param>
    /// <param name="aaFillPaint">Pre-coloured fill paint, AA on.</param>
    /// <param name="strokePaint">Pre-coloured stroke paint, AA on.</param>
    /// <param name="path">Reusable path scratch buffer.</param>
    /// <returns><c>true</c> if rendered, <c>false</c> if the code point is
    /// in range but pictographic and should fall through to the font.</returns>
    public static bool TryDraw(
        SKCanvas canvas,
        int codePoint,
        SKRect rect,
        SKPaint fillPaint,
        SKPaint aaFillPaint,
        SKPaint strokePaint,
        SKPath path)
    {
        switch (codePoint)
        {
            case 0xE0B0: // right hard separator (filled)
                FillTriangle(
                    canvas,
                    aaFillPaint,
                    path,
                    rect.Left,
                    rect.Top,
                    rect.Right,
                    (rect.Top + rect.Bottom) / 2f,
                    rect.Left,
                    rect.Bottom);
                return true;

            case 0xE0B2: // left hard separator (filled)
                FillTriangle(
                    canvas,
                    aaFillPaint,
                    path,
                    rect.Right,
                    rect.Top,
                    rect.Left,
                    (rect.Top + rect.Bottom) / 2f,
                    rect.Right,
                    rect.Bottom);
                return true;

            case 0xE0B1: // right soft separator (line)
                StrokeChevron(canvas, strokePaint, rect, isRight: true);
                return true;

            case 0xE0B3: // left soft separator (line)
                StrokeChevron(canvas, strokePaint, rect, isRight: false);
                return true;

            case 0xE0B4: // right rounded (filled)
                FillRoundedSeparator(canvas, aaFillPaint, path, rect, isRight: true);
                return true;

            case 0xE0B6: // left rounded (filled)
                FillRoundedSeparator(canvas, aaFillPaint, path, rect, isRight: false);
                return true;

            case 0xE0B5: // right rounded (line)
                StrokeRoundedSeparator(canvas, strokePaint, path, rect, isRight: true);
                return true;

            case 0xE0B7: // left rounded (line)
                StrokeRoundedSeparator(canvas, strokePaint, path, rect, isRight: false);
                return true;

            case 0xE0B8: // lower-left filled triangle
                FillTriangle(canvas, aaFillPaint, path, rect.Left, rect.Top, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
                return true;
            case 0xE0BA: // lower-right filled triangle
                FillTriangle(canvas, aaFillPaint, path, rect.Right, rect.Top, rect.Right, rect.Bottom, rect.Left, rect.Bottom);
                return true;
            case 0xE0BC: // upper-left filled triangle
                FillTriangle(canvas, aaFillPaint, path, rect.Left, rect.Top, rect.Right, rect.Top, rect.Left, rect.Bottom);
                return true;
            case 0xE0BE: // upper-right filled triangle
                FillTriangle(canvas, aaFillPaint, path, rect.Left, rect.Top, rect.Right, rect.Top, rect.Right, rect.Bottom);
                return true;

            case 0xE0B9: // lower-left line triangle
                StrokeTriangleHypotenuse(canvas, strokePaint, rect, fromTopLeft: false, leansRight: false);
                return true;
            case 0xE0BB: // lower-right line triangle
                StrokeTriangleHypotenuse(canvas, strokePaint, rect, fromTopLeft: true, leansRight: true);
                return true;
            case 0xE0BD: // upper-left line triangle
                StrokeTriangleHypotenuse(canvas, strokePaint, rect, fromTopLeft: true, leansRight: false);
                return true;
            case 0xE0BF: // upper-right line triangle
                StrokeTriangleHypotenuse(canvas, strokePaint, rect, fromTopLeft: false, leansRight: true);
                return true;
        }

        // Pictographic glyphs (branch E0A0, line-number E0A1, lock E0A2,
        // and the rest of E0Cx/E0Dx) are best left to the font.
        _ = fillPaint;
        return false;
    }

    private static void FillTriangle(SKCanvas canvas, SKPaint fill, SKPath path, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        path.Reset();
        path.MoveTo(x1, y1);
        path.LineTo(x2, y2);
        path.LineTo(x3, y3);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    private static int LineThickness(SKRect rect)
    {
        int t = (int)MathF.Round(rect.Height / 12f);
        return Math.Max(1, t);
    }

    private static void StrokeChevron(SKCanvas canvas, SKPaint strokePaint, SKRect rect, bool isRight)
    {
        strokePaint.StrokeWidth = LineThickness(rect);
        strokePaint.StrokeCap = SKStrokeCap.Square;

        float midY = (rect.Top + rect.Bottom) / 2f;
        if (isRight)
        {
            canvas.DrawLine(rect.Left, rect.Top, rect.Right, midY, strokePaint);
            canvas.DrawLine(rect.Right, midY, rect.Left, rect.Bottom, strokePaint);
        }
        else
        {
            canvas.DrawLine(rect.Right, rect.Top, rect.Left, midY, strokePaint);
            canvas.DrawLine(rect.Left, midY, rect.Right, rect.Bottom, strokePaint);
        }
    }

    private static void FillRoundedSeparator(SKCanvas canvas, SKPaint fill, SKPath path, SKRect rect, bool isRight)
    {
        path.Reset();
        if (isRight)
        {
            // Half-disc opening to the left: the right edge of the cell
            // is the apex; the left edge is the diameter.
            path.MoveTo(rect.Left, rect.Top);
            path.LineTo(rect.Left, rect.Bottom);
            path.ArcTo(
                new SKRect(rect.Left - (rect.Right - rect.Left), rect.Top, rect.Right + (rect.Right - rect.Left), rect.Bottom),
                startAngle: 90f,
                sweepAngle: -90f,
                forceMoveTo: false);
            path.Close();
        }
        else
        {
            path.MoveTo(rect.Right, rect.Top);
            path.LineTo(rect.Right, rect.Bottom);
            path.ArcTo(
                new SKRect(rect.Left - (rect.Right - rect.Left), rect.Top, rect.Right + (rect.Right - rect.Left), rect.Bottom),
                startAngle: 90f,
                sweepAngle: 90f,
                forceMoveTo: false);
            path.Close();
        }

        canvas.DrawPath(path, fill);
    }

    private static void StrokeRoundedSeparator(SKCanvas canvas, SKPaint strokePaint, SKPath path, SKRect rect, bool isRight)
    {
        strokePaint.StrokeWidth = LineThickness(rect);
        strokePaint.StrokeCap = SKStrokeCap.Square;

        path.Reset();
        var ellipseRect = new SKRect(
            rect.Left - (rect.Right - rect.Left),
            rect.Top,
            rect.Right + (rect.Right - rect.Left),
            rect.Bottom);
        if (isRight)
        {
            path.AddArc(ellipseRect, startAngle: 270f, sweepAngle: 90f);
        }
        else
        {
            path.AddArc(ellipseRect, startAngle: 180f, sweepAngle: 90f);
        }

        canvas.DrawPath(path, strokePaint);
    }

    private static void StrokeTriangleHypotenuse(SKCanvas canvas, SKPaint strokePaint, SKRect rect, bool fromTopLeft, bool leansRight)
    {
        strokePaint.StrokeWidth = LineThickness(rect);
        strokePaint.StrokeCap = SKStrokeCap.Square;

        if (fromTopLeft)
        {
            float endX = leansRight ? rect.Right : rect.Right;
            float endY = leansRight ? rect.Bottom : rect.Top;
            if (leansRight)
            {
                // upper-right triangle: hypotenuse from top-left to bottom-right is wrong — actually upper-right line triangle has hypotenuse from top-left going down to bottom-right corner of the upper-right triangle. Use top-left to bottom-right.
                canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, strokePaint);
            }
            else
            {
                // upper-left line triangle: the diagonal hypotenuse runs from top-right down to bottom-left.
                canvas.DrawLine(rect.Right, rect.Top, rect.Left, rect.Bottom, strokePaint);
            }
        }
        else
        {
            if (leansRight)
            {
                // lower-right line triangle: hypotenuse from top-right to bottom-left.
                canvas.DrawLine(rect.Right, rect.Top, rect.Left, rect.Bottom, strokePaint);
            }
            else
            {
                // lower-left line triangle: hypotenuse from top-left to bottom-right.
                canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, strokePaint);
            }
        }
    }
}
