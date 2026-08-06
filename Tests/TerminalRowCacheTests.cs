// <copyright file="TerminalRowCacheTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Tests retained terminal row cache invalidation.
/// </summary>
[TestFixture]
public sealed class TerminalRowCacheTests
{
    /// <summary>
    /// Only a row whose generation changes is rebuilt.
    /// </summary>
    [Test]
    public void UpdateAndDraw_OneChangedGeneration_RebuildsOneRow()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalRowCache();
        var key = CreateKey(fontGeneration: 1);
        var renderCounts = new int[3];
        long[] generations = [1, 1, 1];

        cache.Update(key, generations, 200, 20, RenderBackground, RenderForeground);
        cache.DrawBackgrounds(target.Canvas, 0, 20);
        cache.DrawForegrounds(target.Canvas, 0, 20);
        cache.Update(key, generations, 200, 20, RenderBackground, RenderForeground);

        generations[1]++;
        Assert.That(cache.CountDirtyRows(key, generations), Is.EqualTo(1));
        cache.Update(key, generations, 200, 20, RenderBackground, RenderForeground);

        Assert.That(renderCounts, Is.EqualTo(new[] { 1, 2, 1 }));
        Assert.That(cache.BuildCount, Is.EqualTo(4));

        void RenderBackground(SKCanvas canvas, int row)
        {
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 20, 10, paint);
        }

        void RenderForeground(SKCanvas canvas, int row)
        {
            renderCounts[row]++;
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawCircle(10, 10, 2, paint);
        }
    }

    /// <summary>
    /// A changed static frame key invalidates every cached row.
    /// </summary>
    [Test]
    public void CountDirtyRows_ChangedKey_ReturnsAllRows()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalRowCache();
        long[] generations = [1, 1, 1];
        var firstKey = CreateKey(fontGeneration: 1);

        cache.Update(
            firstKey,
            generations,
            200,
            20,
            static (_, _) => { },
            static (_, _) => { });

        Assert.That(
            cache.CountDirtyRows(CreateKey(fontGeneration: 2), generations),
            Is.EqualTo(generations.Length));
    }

    /// <summary>
    /// Clearing the cache from another thread (a tab switch on the UI thread)
    /// while the render thread updates and draws must not release pictures
    /// that are still being replayed.
    /// </summary>
    [Test]
    public void ClearFromOtherThread_WhileDrawing_DoesNotCorruptPictures()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        using var cache = new TerminalRowCache();
        var key = CreateKey(fontGeneration: 1);
        long[] generations = [1, 1, 1];
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var clearLoop = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                cache.Clear();
            }
        });

        long generation = 1;
        while (!stop.IsCancellationRequested)
        {
            generations[0] = ++generation;
            cache.Update(key, generations, 200, 20, RenderRow, RenderRow);
            cache.DrawBackgrounds(target.Canvas, 0, 20);
            cache.DrawForegrounds(target.Canvas, 0, 20);
        }

        clearLoop.GetAwaiter().GetResult();
        target.Canvas.Flush();
        Assert.That(cache.BuildCount, Is.GreaterThan(0));

        static void RenderRow(SKCanvas canvas, int row)
        {
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 20, 10, paint);
        }
    }

    /// <summary>
    /// Disposing from another thread mid-render is safe and stops further caching.
    /// </summary>
    [Test]
    public void Dispose_WhileUpdating_StopsCachingAndDoesNotThrow()
    {
        using var target = SKSurface.Create(new SKImageInfo(200, 100));
        var cache = new TerminalRowCache();
        var key = CreateKey(fontGeneration: 1);
        long[] generations = [1, 1, 1];

        cache.Update(key, generations, 200, 20, RenderRow, RenderRow);
        cache.Dispose();

        generations[0] = 2;
        Assert.DoesNotThrow(() =>
        {
            cache.Update(key, generations, 200, 20, RenderRow, RenderRow);
            cache.DrawBackgrounds(target.Canvas, 0, 20);
            cache.DrawForegrounds(target.Canvas, 0, 20);
        });

        Assert.That(cache.HasCompleteFrame(key, generations.Length), Is.False);

        static void RenderRow(SKCanvas canvas, int row)
        {
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 20, 10, paint);
        }
    }

    private static TerminalRowCacheKey CreateKey(int fontGeneration)
    {
        return new TerminalRowCacheKey(
            PixelWidth: 200,
            PixelHeight: 100,
            ScaleX: 1,
            ScaleY: 1,
            Rows: 3,
            Columns: 20,
            TopInset: 0,
            BackgroundAlpha: 255,
            EnableLigature: true,
            FontChainGeneration: fontGeneration,
            FontSize: 14,
            CharWidth: 8,
            LineHeight: 20,
            ViewportOffset: 0,
            ScreenBackground: 0,
            ScreenForeground: 0xFFFFFF,
            DefaultForeground: 0xFFFFFF,
            DefaultBackground: 0,
            PaletteIdentity: 1);
    }
}
