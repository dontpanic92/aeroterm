// <copyright file="BoxDrawing.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Programmatic painter for the Box Drawing block (U+2500..U+257F).
/// </summary>
/// <remarks>
/// Each code point is decomposed into per-side edge weights
/// (N / E / S / W &#8594; <see cref="WeightNone"/> / <see cref="WeightLight"/>
/// / <see cref="WeightHeavy"/> / <see cref="WeightDouble"/>), an optional
/// dash segmentation, and a small set of specials (rounded arcs and
/// diagonals). Each edge is painted as a pixel-aligned rectangle that
/// runs from the cell edge to the cell center, extended by the maximum
/// half-thickness on the opposite side so T- and cross-junctions are
/// solid in the middle. Double-line edges are painted as two parallel
/// light strips. Arcs and diagonals are stroked with antialiasing on.
/// This guarantees that runs of `─` join horizontally and runs of `│`
/// join vertically without sub-pixel gaps regardless of the underlying
/// font's natural advance.
/// </remarks>
internal static class BoxDrawing
{
    /// <summary>Edge weight: no edge on this side.</summary>
    public const byte WeightNone = 0;

    /// <summary>Edge weight: a single light stroke.</summary>
    public const byte WeightLight = 1;

    /// <summary>Edge weight: a single heavy stroke.</summary>
    public const byte WeightHeavy = 2;

    /// <summary>Edge weight: two parallel light strokes (double line).</summary>
    public const byte WeightDouble = 3;

    private const byte DashSolid = 0;
    private const byte DashDouble = 2;
    private const byte DashTriple = 3;
    private const byte DashQuadruple = 4;

    private enum Special : byte
    {
        None = 0,
        ArcDownRight,
        ArcDownLeft,
        ArcUpLeft,
        ArcUpRight,
        DiagonalLeanRight,
        DiagonalLeanLeft,
        DiagonalCross,
    }

    /// <summary>
    /// Paints the glyph for the given box-drawing code point.
    /// </summary>
    /// <param name="canvas">Canvas to draw on.</param>
    /// <param name="codePoint">Unicode code point in U+2500..U+257F.</param>
    /// <param name="rect">Cell rect in canvas coordinates.</param>
    /// <param name="fillPaint">Pre-coloured fill paint used for orthogonal
    /// edges. AA must be off for crisp lines.</param>
    /// <param name="strokePaint">Pre-coloured stroke paint used for arcs
    /// and diagonals. AA must be on. <see cref="SKPaint.StrokeWidth"/> is
    /// mutated by this method.</param>
    /// <param name="scratchPath">Reusable path scratch buffer.</param>
    public static void Draw(
        SKCanvas canvas,
        int codePoint,
        SKRect rect,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKPath scratchPath)
    {
        var glyph = Lookup(codePoint);

        if (glyph.Special != Special.None)
        {
            DrawSpecial(canvas, glyph.Special, rect, fillPaint, strokePaint, scratchPath);
            return;
        }

        DrawEdges(canvas, glyph, rect, fillPaint);
    }

    private static int LightThickness(SKRect rect)
    {
        int t = (int)MathF.Round(rect.Height / 12f);
        return Math.Max(1, t);
    }

    private static int HeavyThickness(SKRect rect)
    {
        int light = LightThickness(rect);
        return Math.Max(light + 1, light * 2);
    }

    private static void DrawEdges(SKCanvas canvas, BoxGlyph g, SKRect rect, SKPaint fillPaint)
    {
        int light = LightThickness(rect);
        int heavy = HeavyThickness(rect);

        // Snap cell mid-points to integer pixels. With even cell width,
        // (Right - Left) is even and Floor(W/2) gives the exact halfway
        // pixel boundary so a 1-pixel light stroke at (cy - 0) lands on
        // an integer row.
        float cx = rect.Left + (float)Math.Floor((rect.Right - rect.Left) / 2);
        float cy = rect.Top + (float)Math.Floor((rect.Bottom - rect.Top) / 2);

        // The maximum half-thickness across present sides defines the
        // overlap inside the cell center so junctions stay solid.
        int maxHalf = MaxHalf(g, light, heavy);

        DrawHorizontalEdge(canvas, fillPaint, g.W, isWest: true, rect, cx, cy, light, heavy, maxHalf, g.Dash);
        DrawHorizontalEdge(canvas, fillPaint, g.E, isWest: false, rect, cx, cy, light, heavy, maxHalf, g.Dash);
        DrawVerticalEdge(canvas, fillPaint, g.N, isNorth: true, rect, cx, cy, light, heavy, maxHalf, g.Dash);
        DrawVerticalEdge(canvas, fillPaint, g.S, isNorth: false, rect, cx, cy, light, heavy, maxHalf, g.Dash);
    }

    private static int MaxHalf(BoxGlyph g, int light, int heavy)
    {
        int maxHalf = 0;
        maxHalf = Math.Max(maxHalf, HalfFor(g.N, light, heavy));
        maxHalf = Math.Max(maxHalf, HalfFor(g.E, light, heavy));
        maxHalf = Math.Max(maxHalf, HalfFor(g.S, light, heavy));
        maxHalf = Math.Max(maxHalf, HalfFor(g.W, light, heavy));
        return maxHalf;
    }

    private static int HalfFor(byte weight, int light, int heavy)
    {
        return weight switch
        {
            WeightLight => (light + 1) / 2,
            WeightHeavy => (heavy + 1) / 2,

            // Double: outer extents span (light + gap + light); half is
            // (gap/2 + light).
            WeightDouble => light + ((light + 1) / 2),
            _ => 0,
        };
    }

    private static void DrawHorizontalEdge(
        SKCanvas canvas,
        SKPaint fillPaint,
        byte weight,
        bool isWest,
        SKRect rect,
        float cx,
        float cy,
        int light,
        int heavy,
        int maxHalf,
        byte dash)
    {
        if (weight == WeightNone)
        {
            return;
        }

        if (weight == WeightDouble)
        {
            int gap = light;
            float topY = cy - light - (gap / 2f);
            float botY = cy + (gap / 2f);
            DrawHorizontalStrip(canvas, fillPaint, isWest, rect, cx, topY, light, maxHalf, dash);
            DrawHorizontalStrip(canvas, fillPaint, isWest, rect, cx, botY, light, maxHalf, dash);
            return;
        }

        int t = weight == WeightHeavy ? heavy : light;
        float y = cy - (t / 2f);
        DrawHorizontalStrip(canvas, fillPaint, isWest, rect, cx, y, t, maxHalf, dash);
    }

    private static void DrawVerticalEdge(
        SKCanvas canvas,
        SKPaint fillPaint,
        byte weight,
        bool isNorth,
        SKRect rect,
        float cx,
        float cy,
        int light,
        int heavy,
        int maxHalf,
        byte dash)
    {
        if (weight == WeightNone)
        {
            return;
        }

        if (weight == WeightDouble)
        {
            int gap = light;
            float leftX = cx - light - (gap / 2f);
            float rightX = cx + (gap / 2f);
            DrawVerticalStrip(canvas, fillPaint, isNorth, rect, cy, leftX, light, maxHalf, dash);
            DrawVerticalStrip(canvas, fillPaint, isNorth, rect, cy, rightX, light, maxHalf, dash);
            return;
        }

        int t = weight == WeightHeavy ? heavy : light;
        float x = cx - (t / 2f);
        DrawVerticalStrip(canvas, fillPaint, isNorth, rect, cy, x, t, maxHalf, dash);
    }

    private static void DrawHorizontalStrip(
        SKCanvas canvas,
        SKPaint fillPaint,
        bool isWest,
        SKRect rect,
        float cx,
        float y,
        int thickness,
        int maxHalf,
        byte dash)
    {
        float startX = isWest ? rect.Left : cx - maxHalf;
        float endX = isWest ? cx + maxHalf : rect.Right;

        if (dash == DashSolid)
        {
            canvas.DrawRect(new SKRect(startX, y, endX, y + thickness), fillPaint);
        }
        else
        {
            DrawHorizontalDashed(canvas, fillPaint, startX, endX, y, thickness, dash);
        }
    }

    private static void DrawVerticalStrip(
        SKCanvas canvas,
        SKPaint fillPaint,
        bool isNorth,
        SKRect rect,
        float cy,
        float x,
        int thickness,
        int maxHalf,
        byte dash)
    {
        float startY = isNorth ? rect.Top : cy - maxHalf;
        float endY = isNorth ? cy + maxHalf : rect.Bottom;

        if (dash == DashSolid)
        {
            canvas.DrawRect(new SKRect(x, startY, x + thickness, endY), fillPaint);
        }
        else
        {
            DrawVerticalDashed(canvas, fillPaint, x, startY, endY, thickness, dash);
        }
    }

    private static void DrawHorizontalDashed(SKCanvas canvas, SKPaint fillPaint, float startX, float endX, float y, int thickness, int segments)
    {
        float length = endX - startX;
        if (length <= 0)
        {
            return;
        }

        // 2*segments slots alternating dash/gap.
        int slots = segments * 2;
        float slot = length / slots;
        for (int i = 0; i < slots; i += 2)
        {
            float x0 = startX + (i * slot);
            float x1 = x0 + slot;
            canvas.DrawRect(new SKRect(x0, y, x1, y + thickness), fillPaint);
        }
    }

    private static void DrawVerticalDashed(SKCanvas canvas, SKPaint fillPaint, float x, float startY, float endY, int thickness, int segments)
    {
        float length = endY - startY;
        if (length <= 0)
        {
            return;
        }

        int slots = segments * 2;
        float slot = length / slots;
        for (int i = 0; i < slots; i += 2)
        {
            float y0 = startY + (i * slot);
            float y1 = y0 + slot;
            canvas.DrawRect(new SKRect(x, y0, x + thickness, y1), fillPaint);
        }
    }

    private static void DrawSpecial(
        SKCanvas canvas,
        Special special,
        SKRect rect,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKPath path)
    {
        int light = LightThickness(rect);
        float cx = rect.Left + (float)Math.Floor((rect.Right - rect.Left) / 2);
        float cy = rect.Top + (float)Math.Floor((rect.Bottom - rect.Top) / 2);

        switch (special)
        {
            // ╭ : horizontal goes RIGHT from center, vertical goes DOWN.
            //     arc curves through the upper-left interior of the cell.
            case Special.ArcDownRight:
                DrawArc(canvas, rect, strokePaint, path, cx, cy, light, hRight: true, vDown: true);
                break;

            // ╮ : horizontal goes LEFT, vertical goes DOWN.
            case Special.ArcDownLeft:
                DrawArc(canvas, rect, strokePaint, path, cx, cy, light, hRight: false, vDown: true);
                break;

            // ╯ : horizontal goes LEFT, vertical goes UP.
            case Special.ArcUpLeft:
                DrawArc(canvas, rect, strokePaint, path, cx, cy, light, hRight: false, vDown: false);
                break;

            // ╰ : horizontal goes RIGHT, vertical goes UP.
            case Special.ArcUpRight:
                DrawArc(canvas, rect, strokePaint, path, cx, cy, light, hRight: true, vDown: false);
                break;

            case Special.DiagonalLeanRight:
                DrawDiagonal(canvas, rect.Left, rect.Bottom, rect.Right, rect.Top, light, strokePaint);
                break;
            case Special.DiagonalLeanLeft:
                DrawDiagonal(canvas, rect.Left, rect.Top, rect.Right, rect.Bottom, light, strokePaint);
                break;
            case Special.DiagonalCross:
                DrawDiagonal(canvas, rect.Left, rect.Bottom, rect.Right, rect.Top, light, strokePaint);
                DrawDiagonal(canvas, rect.Left, rect.Top, rect.Right, rect.Bottom, light, strokePaint);
                break;
        }

        _ = fillPaint;
    }

    private static void DrawArc(
        SKCanvas canvas,
        SKRect rect,
        SKPaint strokePaint,
        SKPath path,
        float cx,
        float cy,
        int light,
        bool hRight,
        bool vDown)
    {
        // Draw the entire rounded corner — both straight stubs AND the
        // arc — as ONE continuous stroked path. This is the only way to
        // avoid sub-pixel discontinuities at the curve-to-stub join: any
        // composite of separately-painted shapes leaves visible 1-pixel
        // tails or AA mismatches because the arc geometry naturally
        // extends past the strip boundary as it curves.
        //
        // Pixel-grid alignment for the AA-on stroke. The neighbouring
        // straight `─`/`│` cells render via AA-off rects whose top edge
        // lies at `cy - light/2`. To match that, the AA-on stroke
        // centerline must sit at the geometric center of the same `light`
        // pixel rows. For odd `light` that center is at half-pixel grid
        // (int + 0.5); for even `light` it sits on the integer grid.
        float gridOffset = (light & 1) * 0.5f;
        float pcx = cx - gridOffset;
        float pcy = cy - gridOffset;

        // Pick a corner radius that produces a visible curve while
        // leaving room for the straight stubs to extend back to the cell
        // edges. Half the smaller half-cell axis works well at typical
        // terminal cell sizes.
        float halfW = (rect.Right - rect.Left) / 2f;
        float halfH = (rect.Bottom - rect.Top) / 2f;
        float radius = Math.Max(light, Math.Min(halfW, halfH) * 0.5f);

        float horizEdgeX = hRight ? rect.Right : rect.Left;
        float vertEdgeY = vDown ? rect.Bottom : rect.Top;
        float arcStartX = hRight ? pcx + radius : pcx - radius;
        float arcEndY = vDown ? pcy + radius : pcy - radius;

        // Path: cell-edge → straight stub → quarter arc → straight stub
        // → opposite cell-edge. The arc is implemented as a quadratic
        // Bézier with the control point at the would-be sharp corner;
        // this gives a smooth circular-looking curve whose tangents at
        // both endpoints are exactly horizontal and vertical, so the
        // straight segments continue the curve without any kink.
        path.Reset();
        path.MoveTo(horizEdgeX, pcy);
        path.LineTo(arcStartX, pcy);
        path.QuadTo(pcx, pcy, pcx, arcEndY);
        path.LineTo(pcx, vertEdgeY);

        strokePaint.StrokeWidth = light;
        strokePaint.StrokeCap = SKStrokeCap.Butt;
        strokePaint.StrokeJoin = SKStrokeJoin.Round;
        canvas.DrawPath(path, strokePaint);
    }

    private static void DrawDiagonal(SKCanvas canvas, float x1, float y1, float x2, float y2, int light, SKPaint strokePaint)
    {
        strokePaint.StrokeWidth = light;
        strokePaint.StrokeCap = SKStrokeCap.Square;
        canvas.DrawLine(x1, y1, x2, y2, strokePaint);
    }

    private static BoxGlyph Lookup(int cp)
    {
        switch (cp)
        {
            // Pure horizontal/vertical lines.
            case 0x2500: return Edges(WeightNone, WeightLight, WeightNone, WeightLight);
            case 0x2501: return Edges(WeightNone, WeightHeavy, WeightNone, WeightHeavy);
            case 0x2502: return Edges(WeightLight, WeightNone, WeightLight, WeightNone);
            case 0x2503: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightNone);

            // Triple/quadruple-dash variants.
            case 0x2504: return Edges(WeightNone, WeightLight, WeightNone, WeightLight, DashTriple);
            case 0x2505: return Edges(WeightNone, WeightHeavy, WeightNone, WeightHeavy, DashTriple);
            case 0x2506: return Edges(WeightLight, WeightNone, WeightLight, WeightNone, DashTriple);
            case 0x2507: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightNone, DashTriple);
            case 0x2508: return Edges(WeightNone, WeightLight, WeightNone, WeightLight, DashQuadruple);
            case 0x2509: return Edges(WeightNone, WeightHeavy, WeightNone, WeightHeavy, DashQuadruple);
            case 0x250A: return Edges(WeightLight, WeightNone, WeightLight, WeightNone, DashQuadruple);
            case 0x250B: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightNone, DashQuadruple);

            // Down+right corners.
            case 0x250C: return Edges(WeightNone, WeightLight, WeightLight, WeightNone);
            case 0x250D: return Edges(WeightNone, WeightHeavy, WeightLight, WeightNone);
            case 0x250E: return Edges(WeightNone, WeightLight, WeightHeavy, WeightNone);
            case 0x250F: return Edges(WeightNone, WeightHeavy, WeightHeavy, WeightNone);

            // Down+left corners.
            case 0x2510: return Edges(WeightNone, WeightNone, WeightLight, WeightLight);
            case 0x2511: return Edges(WeightNone, WeightNone, WeightLight, WeightHeavy);
            case 0x2512: return Edges(WeightNone, WeightNone, WeightHeavy, WeightLight);
            case 0x2513: return Edges(WeightNone, WeightNone, WeightHeavy, WeightHeavy);

            // Up+right corners.
            case 0x2514: return Edges(WeightLight, WeightLight, WeightNone, WeightNone);
            case 0x2515: return Edges(WeightLight, WeightHeavy, WeightNone, WeightNone);
            case 0x2516: return Edges(WeightHeavy, WeightLight, WeightNone, WeightNone);
            case 0x2517: return Edges(WeightHeavy, WeightHeavy, WeightNone, WeightNone);

            // Up+left corners.
            case 0x2518: return Edges(WeightLight, WeightNone, WeightNone, WeightLight);
            case 0x2519: return Edges(WeightLight, WeightNone, WeightNone, WeightHeavy);
            case 0x251A: return Edges(WeightHeavy, WeightNone, WeightNone, WeightLight);
            case 0x251B: return Edges(WeightHeavy, WeightNone, WeightNone, WeightHeavy);

            // Vertical+right tee.
            case 0x251C: return Edges(WeightLight, WeightLight, WeightLight, WeightNone);
            case 0x251D: return Edges(WeightLight, WeightHeavy, WeightLight, WeightNone);
            case 0x251E: return Edges(WeightHeavy, WeightLight, WeightLight, WeightNone);
            case 0x251F: return Edges(WeightLight, WeightLight, WeightHeavy, WeightNone);
            case 0x2520: return Edges(WeightHeavy, WeightLight, WeightHeavy, WeightNone);
            case 0x2521: return Edges(WeightHeavy, WeightHeavy, WeightLight, WeightNone);
            case 0x2522: return Edges(WeightLight, WeightHeavy, WeightHeavy, WeightNone);
            case 0x2523: return Edges(WeightHeavy, WeightHeavy, WeightHeavy, WeightNone);

            // Vertical+left tee.
            case 0x2524: return Edges(WeightLight, WeightNone, WeightLight, WeightLight);
            case 0x2525: return Edges(WeightLight, WeightNone, WeightLight, WeightHeavy);
            case 0x2526: return Edges(WeightHeavy, WeightNone, WeightLight, WeightLight);
            case 0x2527: return Edges(WeightLight, WeightNone, WeightHeavy, WeightLight);
            case 0x2528: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightLight);
            case 0x2529: return Edges(WeightHeavy, WeightNone, WeightLight, WeightHeavy);
            case 0x252A: return Edges(WeightLight, WeightNone, WeightHeavy, WeightHeavy);
            case 0x252B: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightHeavy);

            // Down+horizontal tee.
            case 0x252C: return Edges(WeightNone, WeightLight, WeightLight, WeightLight);
            case 0x252D: return Edges(WeightNone, WeightLight, WeightLight, WeightHeavy);
            case 0x252E: return Edges(WeightNone, WeightHeavy, WeightLight, WeightLight);
            case 0x252F: return Edges(WeightNone, WeightHeavy, WeightLight, WeightHeavy);
            case 0x2530: return Edges(WeightNone, WeightLight, WeightHeavy, WeightLight);
            case 0x2531: return Edges(WeightNone, WeightLight, WeightHeavy, WeightHeavy);
            case 0x2532: return Edges(WeightNone, WeightHeavy, WeightHeavy, WeightLight);
            case 0x2533: return Edges(WeightNone, WeightHeavy, WeightHeavy, WeightHeavy);

            // Up+horizontal tee.
            case 0x2534: return Edges(WeightLight, WeightLight, WeightNone, WeightLight);
            case 0x2535: return Edges(WeightLight, WeightLight, WeightNone, WeightHeavy);
            case 0x2536: return Edges(WeightLight, WeightHeavy, WeightNone, WeightLight);
            case 0x2537: return Edges(WeightLight, WeightHeavy, WeightNone, WeightHeavy);
            case 0x2538: return Edges(WeightHeavy, WeightLight, WeightNone, WeightLight);
            case 0x2539: return Edges(WeightHeavy, WeightLight, WeightNone, WeightHeavy);
            case 0x253A: return Edges(WeightHeavy, WeightHeavy, WeightNone, WeightLight);
            case 0x253B: return Edges(WeightHeavy, WeightHeavy, WeightNone, WeightHeavy);

            // Cross.
            case 0x253C: return Edges(WeightLight, WeightLight, WeightLight, WeightLight);
            case 0x253D: return Edges(WeightLight, WeightLight, WeightLight, WeightHeavy);
            case 0x253E: return Edges(WeightLight, WeightHeavy, WeightLight, WeightLight);
            case 0x253F: return Edges(WeightLight, WeightHeavy, WeightLight, WeightHeavy);
            case 0x2540: return Edges(WeightHeavy, WeightLight, WeightLight, WeightLight);
            case 0x2541: return Edges(WeightLight, WeightLight, WeightHeavy, WeightLight);
            case 0x2542: return Edges(WeightHeavy, WeightLight, WeightHeavy, WeightLight);
            case 0x2543: return Edges(WeightHeavy, WeightLight, WeightLight, WeightHeavy);
            case 0x2544: return Edges(WeightHeavy, WeightHeavy, WeightLight, WeightLight);
            case 0x2545: return Edges(WeightLight, WeightLight, WeightHeavy, WeightHeavy);
            case 0x2546: return Edges(WeightLight, WeightHeavy, WeightHeavy, WeightLight);
            case 0x2547: return Edges(WeightHeavy, WeightHeavy, WeightLight, WeightHeavy);
            case 0x2548: return Edges(WeightLight, WeightHeavy, WeightHeavy, WeightHeavy);
            case 0x2549: return Edges(WeightHeavy, WeightLight, WeightHeavy, WeightHeavy);
            case 0x254A: return Edges(WeightHeavy, WeightHeavy, WeightHeavy, WeightLight);
            case 0x254B: return Edges(WeightHeavy, WeightHeavy, WeightHeavy, WeightHeavy);

            // Double-dash variants.
            case 0x254C: return Edges(WeightNone, WeightLight, WeightNone, WeightLight, DashDouble);
            case 0x254D: return Edges(WeightNone, WeightHeavy, WeightNone, WeightHeavy, DashDouble);
            case 0x254E: return Edges(WeightLight, WeightNone, WeightLight, WeightNone, DashDouble);
            case 0x254F: return Edges(WeightHeavy, WeightNone, WeightHeavy, WeightNone, DashDouble);

            // Double lines and junctions.
            case 0x2550: return Edges(WeightNone, WeightDouble, WeightNone, WeightDouble);
            case 0x2551: return Edges(WeightDouble, WeightNone, WeightDouble, WeightNone);
            case 0x2552: return Edges(WeightNone, WeightDouble, WeightLight, WeightNone);
            case 0x2553: return Edges(WeightNone, WeightLight, WeightDouble, WeightNone);
            case 0x2554: return Edges(WeightNone, WeightDouble, WeightDouble, WeightNone);
            case 0x2555: return Edges(WeightNone, WeightNone, WeightLight, WeightDouble);
            case 0x2556: return Edges(WeightNone, WeightNone, WeightDouble, WeightLight);
            case 0x2557: return Edges(WeightNone, WeightNone, WeightDouble, WeightDouble);
            case 0x2558: return Edges(WeightLight, WeightDouble, WeightNone, WeightNone);
            case 0x2559: return Edges(WeightDouble, WeightLight, WeightNone, WeightNone);
            case 0x255A: return Edges(WeightDouble, WeightDouble, WeightNone, WeightNone);
            case 0x255B: return Edges(WeightLight, WeightNone, WeightNone, WeightDouble);
            case 0x255C: return Edges(WeightDouble, WeightNone, WeightNone, WeightLight);
            case 0x255D: return Edges(WeightDouble, WeightNone, WeightNone, WeightDouble);
            case 0x255E: return Edges(WeightLight, WeightDouble, WeightLight, WeightNone);
            case 0x255F: return Edges(WeightDouble, WeightLight, WeightDouble, WeightNone);
            case 0x2560: return Edges(WeightDouble, WeightDouble, WeightDouble, WeightNone);
            case 0x2561: return Edges(WeightLight, WeightNone, WeightLight, WeightDouble);
            case 0x2562: return Edges(WeightDouble, WeightNone, WeightDouble, WeightLight);
            case 0x2563: return Edges(WeightDouble, WeightNone, WeightDouble, WeightDouble);
            case 0x2564: return Edges(WeightNone, WeightDouble, WeightLight, WeightDouble);
            case 0x2565: return Edges(WeightNone, WeightLight, WeightDouble, WeightLight);
            case 0x2566: return Edges(WeightNone, WeightDouble, WeightDouble, WeightDouble);
            case 0x2567: return Edges(WeightLight, WeightDouble, WeightNone, WeightDouble);
            case 0x2568: return Edges(WeightDouble, WeightLight, WeightNone, WeightLight);
            case 0x2569: return Edges(WeightDouble, WeightDouble, WeightNone, WeightDouble);
            case 0x256A: return Edges(WeightLight, WeightDouble, WeightLight, WeightDouble);
            case 0x256B: return Edges(WeightDouble, WeightLight, WeightDouble, WeightLight);
            case 0x256C: return Edges(WeightDouble, WeightDouble, WeightDouble, WeightDouble);

            // Arcs.
            case 0x256D: return SpecialGlyph(Special.ArcDownRight);
            case 0x256E: return SpecialGlyph(Special.ArcDownLeft);
            case 0x256F: return SpecialGlyph(Special.ArcUpLeft);
            case 0x2570: return SpecialGlyph(Special.ArcUpRight);

            // Diagonals.
            case 0x2571: return SpecialGlyph(Special.DiagonalLeanRight);
            case 0x2572: return SpecialGlyph(Special.DiagonalLeanLeft);
            case 0x2573: return SpecialGlyph(Special.DiagonalCross);

            // Light end caps.
            case 0x2574: return Edges(WeightNone, WeightNone, WeightNone, WeightLight);
            case 0x2575: return Edges(WeightLight, WeightNone, WeightNone, WeightNone);
            case 0x2576: return Edges(WeightNone, WeightLight, WeightNone, WeightNone);
            case 0x2577: return Edges(WeightNone, WeightNone, WeightLight, WeightNone);

            // Heavy end caps.
            case 0x2578: return Edges(WeightNone, WeightNone, WeightNone, WeightHeavy);
            case 0x2579: return Edges(WeightHeavy, WeightNone, WeightNone, WeightNone);
            case 0x257A: return Edges(WeightNone, WeightHeavy, WeightNone, WeightNone);
            case 0x257B: return Edges(WeightNone, WeightNone, WeightHeavy, WeightNone);

            // Mixed-weight half-lines.
            case 0x257C: return Edges(WeightNone, WeightHeavy, WeightNone, WeightLight);
            case 0x257D: return Edges(WeightNone, WeightNone, WeightHeavy, WeightLight);
            case 0x257E: return Edges(WeightNone, WeightLight, WeightNone, WeightHeavy);
            case 0x257F: return Edges(WeightHeavy, WeightNone, WeightLight, WeightNone);

            default: return default;
        }
    }

    private static BoxGlyph Edges(byte n, byte e, byte s, byte w, byte dash = DashSolid)
        => new() { N = n, E = e, S = s, W = w, Dash = dash, Special = Special.None };

    private static BoxGlyph SpecialGlyph(Special special)
        => new() { Special = special };

    private struct BoxGlyph
    {
        public byte N;
        public byte E;
        public byte S;
        public byte W;
        public byte Dash;
        public Special Special;
    }
}
