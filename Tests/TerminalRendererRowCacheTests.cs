// <copyright file="TerminalRendererRowCacheTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Controls;
using AeroTerm.Controls.Terminal;
using AeroTerm.Pty;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Pixel parity tests for retained row rendering.
/// </summary>
[TestFixture]
public sealed class TerminalRendererRowCacheTests
{
    /// <summary>
    /// Cached background and foreground row pictures reproduce the direct
    /// static renderer output.
    /// </summary>
    [AvaloniaTest]
    public void RowPictures_MatchDirectStaticRender()
    {
        var buffer = new TerminalBuffer(8, 3);
        buffer.RecolorDefaults(0xF0F0F0, 0x101010);
        var parser = new VtParser(buffer, _ => { });
        parser.Process(Encoding.UTF8.GetBytes("plain\r\n\x1B[31;44mcolor\x1B[0m\r\nlast"));
        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);

        using var fontChain = new FontFallbackChain();
        string fontName = SKTypeface.Default.FamilyName;
        fontChain.Rebuild([fontName]);
        using var shaper = new LigatureTextShaper();
        var imeClient = new EditorTextInputMethodClient(new Control());
        using var renderer = new TerminalRenderer(fontChain, shaper, imeClient);
        var textParam = new TextLayoutParameters(fontName, 11);
        int width = (int)Math.Ceiling(screen!.Cells.GetLength(1) * textParam.CharWidth);
        int height = (int)Math.Ceiling(screen.Cells.GetLength(0) * textParam.LineHeight);
        using var directBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cachedBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var directCanvas = new SKCanvas(directBitmap);
        using var cachedCanvas = new SKCanvas(cachedBitmap);

        renderer.Render(
            directCanvas,
            screen,
            textParam,
            modeInfo: null,
            enableLigature: false,
            backgroundAlpha: 255,
            shouldDrawCursor: false,
            drawDynamicOverlays: false);

        using var cache = new TerminalRowCache();
        var key = CreateKey(screen, textParam, width, height);
        cache.Update(
            key,
            screen.RowGenerations,
            width,
            textParam.LineHeight,
            (canvas, row) => renderer.RenderStaticBackgroundRow(canvas, screen, row, textParam),
            (canvas, row) => renderer.RenderStaticForegroundRow(canvas, screen, row, textParam, enableLigature: false));
        cachedCanvas.Clear(TerminalRenderer.GetSkColor(screen.BackgroundColor));
        cache.DrawBackgrounds(cachedCanvas, 0, textParam.LineHeight);
        renderer.RenderMiddleOverlays(
            cachedCanvas,
            screen,
            textParam,
            topInset: 0,
            selection: null,
            selectionColor: default,
            selectionRowOffset: 0,
            searchMatches: null);
        cache.DrawForegrounds(cachedCanvas, 0, textParam.LineHeight);
        renderer.RenderHyperlinkOverlay(cachedCanvas, screen, textParam, topInset: 0, hyperlinkRun: null);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.That(
                    cachedBitmap.GetPixel(x, y),
                    Is.EqualTo(directBitmap.GetPixel(x, y)),
                    $"Pixel mismatch at ({x}, {y}).");
            }
        }
    }

    private static TerminalRowCacheKey CreateKey(
        Screen screen,
        TextLayoutParameters textParam,
        int width,
        int height)
    {
        return new TerminalRowCacheKey(
            PixelWidth: width,
            PixelHeight: height,
            ScaleX: 1,
            ScaleY: 1,
            Rows: screen.Cells.GetLength(0),
            Columns: screen.Cells.GetLength(1),
            TopInset: 0,
            BackgroundAlpha: 255,
            EnableLigature: false,
            FontChainGeneration: 1,
            FontSize: textParam.SkiaFontSize,
            CharWidth: textParam.CharWidth,
            LineHeight: textParam.LineHeight,
            ViewportOffset: 0,
            ScreenBackground: screen.BackgroundColor,
            ScreenForeground: screen.ForegroundColor,
            DefaultForeground: screen.Palette.DefaultForeground,
            DefaultBackground: screen.Palette.DefaultBackground,
            PaletteIdentity: 1);
    }
}
