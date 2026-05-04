// <copyright file="SkiaColorEmojiSmokeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Runtime.InteropServices;
using NUnit.Framework;
using SkiaSharp;

/// <summary>
/// Smoke tests for the Skia color emoji capability used by tab title rendering.
/// </summary>
[TestFixture]
public class SkiaColorEmojiSmokeTests
{
    /// <summary>
    /// Direct SkiaSharp rendering can produce colored Segoe UI Emoji pixels on
    /// Windows, so the tab title workaround does not need DirectWrite.
    /// </summary>
    [Test]
    public void DrawText_SegoeUiEmoji_OnWindowsProducesColoredPixels()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Windows Segoe UI Emoji color rendering only applies on Windows.");
        }

        using var typeface = SKTypeface.FromFamilyName("Segoe UI Emoji");
        Assert.That(typeface, Is.Not.Null);

        using var bitmap = new SKBitmap(96, 72, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var font = new SKFont(typeface, 48)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        canvas.DrawText("😀", 8, 54, font, paint);

        int coloredPixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Alpha > 0 && (color.Red != color.Green || color.Green != color.Blue))
                {
                    coloredPixels++;
                }
            }
        }

        Assert.That(coloredPixels, Is.GreaterThan(0));
    }
}
