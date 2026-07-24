// <copyright file="TerminalFrameCacheTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Tests static terminal frame cache reuse and invalidation.
/// </summary>
[TestFixture]
public sealed class TerminalFrameCacheTests
{
    /// <summary>
    /// Cursor-only frames reuse the cached static terminal image.
    /// </summary>
    [Test]
    public void Draw_UnchangedStaticFrame_ReusesCachedImage()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalFrameCache();
        var key = CreateKey(fontGeneration: 1);
        int renders = 0;

        cache.RebuildAndDraw(target.Canvas, new SKRect(0, 0, 200, 100), key, RenderFrame);
        bool reused = cache.TryDraw(target.Canvas, new SKRect(0, 0, 200, 100), key);

        Assert.That(renders, Is.EqualTo(1));
        Assert.That(cache.BuildCount, Is.EqualTo(1));
        Assert.That(reused, Is.True);

        void RenderFrame(SKCanvas canvas)
        {
            renders++;
            canvas.Clear(SKColors.Black);
        }
    }

    /// <summary>
    /// A missing cached image is reported without rebuilding implicitly.
    /// </summary>
    [Test]
    public void TryDraw_MissingImage_ReturnsFalse()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalFrameCache();
        var key = CreateKey(fontGeneration: 1);

        bool reused = cache.TryDraw(target.Canvas, new SKRect(0, 0, 200, 100), key);

        Assert.That(reused, Is.False);
        Assert.That(cache.BuildCount, Is.Zero);
    }

    /// <summary>
    /// A font or overlay key change rebuilds the cached frame.
    /// </summary>
    [Test]
    public void Draw_ChangedKey_RebuildsCachedImage()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalFrameCache();
        int renders = 0;

        cache.RebuildAndDraw(target.Canvas, new SKRect(0, 0, 200, 100), CreateKey(fontGeneration: 1), _ => renders++);
        bool reused = cache.TryDraw(
            target.Canvas,
            new SKRect(0, 0, 200, 100),
            CreateKey(fontGeneration: 2));

        Assert.That(renders, Is.EqualTo(1));
        Assert.That(cache.BuildCount, Is.EqualTo(1));
        Assert.That(reused, Is.False);
    }

    private static TerminalFrameCacheKey CreateKey(int fontGeneration)
    {
        return new TerminalFrameCacheKey(
            PixelWidth: 200,
            PixelHeight: 100,
            ScaleX: 1,
            ScaleY: 1,
            Rows: 5,
            Columns: 20,
            TopInset: 0,
            BackgroundAlpha: 255,
            EnableLigature: true,
            FontChainGeneration: fontGeneration,
            FontSize: 14,
            CharWidth: 8,
            LineHeight: 18,
            ViewportOffset: 0,
            SelectionHash: 0,
            SelectionRowOffset: 0,
            SelectionColor: 0,
            SearchMatchesIdentity: 0,
            ActiveSearchMatchIndex: -1,
            HyperlinkHash: 0,
            ScreenBackground: 0,
            ScreenForeground: 0xFFFFFF,
            DefaultForeground: 0xFFFFFF,
            DefaultBackground: 0,
            PaletteIdentity: 1);
    }
}
