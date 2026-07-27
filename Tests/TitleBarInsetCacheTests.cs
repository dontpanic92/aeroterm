// <copyright file="TitleBarInsetCacheTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Tests title-bar inset cache reuse and invalidation.
/// </summary>
[TestFixture]
public sealed class TitleBarInsetCacheTests
{
    /// <summary>
    /// Repeated draws with an unchanged key reuse the completed blur image.
    /// </summary>
    [Test]
    public void Draw_UnchangedKey_ReusesCachedImage()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 40));
        using var cache = new TitleBarInsetCache(14);
        var key = CreateKey(ghostContentHash: 1);
        int renders = 0;

        cache.Draw(target.Canvas, new SKRect(0, 0, 200, 40), key, DrawContent);
        cache.Draw(target.Canvas, new SKRect(0, 0, 200, 40), key, DrawContent);

        Assert.That(renders, Is.EqualTo(1));
        Assert.That(cache.BuildCount, Is.EqualTo(1));

        void DrawContent(SKCanvas canvas)
        {
            renders++;
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(10, 10, 20, 10, paint);
        }
    }

    /// <summary>
    /// A changed ghost-row content hash rebuilds the cached image.
    /// </summary>
    [Test]
    public void Draw_ChangedKey_RebuildsCachedImage()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 40));
        using var cache = new TitleBarInsetCache(14);
        int renders = 0;

        cache.Draw(target.Canvas, new SKRect(0, 0, 200, 40), CreateKey(ghostContentHash: 1), _ => renders++);
        cache.Draw(target.Canvas, new SKRect(0, 0, 200, 40), CreateKey(ghostContentHash: 2), _ => renders++);

        Assert.That(renders, Is.EqualTo(2));
        Assert.That(cache.BuildCount, Is.EqualTo(2));
    }

    private static TitleBarInsetCacheKey CreateKey(int ghostContentHash)
    {
        return new TitleBarInsetCacheKey(
            PixelWidth: 200,
            PixelHeight: 40,
            ScaleX: 1,
            ScaleY: 1,
            GhostRowCount: 2,
            GhostContentHash: ghostContentHash,
            FontSize: 14,
            CharWidth: 8,
            LineHeight: 16,
            TypefaceHandle: 1);
    }
}
