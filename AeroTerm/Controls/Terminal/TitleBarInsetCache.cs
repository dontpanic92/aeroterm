// <copyright file="TitleBarInsetCache.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using AeroTerm.Diagnostics;
using SkiaSharp;

/// <summary>
/// Caches the blurred title-bar inset as a raster image so ordinary terminal
/// frames only draw the completed image.
/// </summary>
internal sealed class TitleBarInsetCache : IDisposable
{
    private readonly float blurSigma;
    private SKImage? image;
    private TitleBarInsetCacheKey key;
    private bool hasKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBarInsetCache"/> class.
    /// </summary>
    /// <param name="blurSigma">Blur radius in device-independent pixels.</param>
    public TitleBarInsetCache(float blurSigma)
    {
        if (blurSigma < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blurSigma));
        }

        this.blurSigma = blurSigma;
    }

    /// <summary>
    /// Gets the number of cache image builds. Exposed for tests and diagnostics.
    /// </summary>
    internal int BuildCount { get; private set; }

    /// <summary>
    /// Draws the cached inset, rebuilding it when the supplied key changes.
    /// </summary>
    /// <param name="target">Target frame canvas.</param>
    /// <param name="destination">Destination rectangle in target coordinates.</param>
    /// <param name="key">Complete cache dependency key.</param>
    /// <param name="renderContent">Draws unblurred inset content in device-independent coordinates.</param>
    public void Draw(
        SKCanvas target,
        SKRect destination,
        TitleBarInsetCacheKey key,
        Action<SKCanvas> renderContent)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(renderContent);

        if (key.PixelWidth <= 0 || key.PixelHeight <= 0)
        {
            this.Clear();
            return;
        }

        if (!this.hasKey || this.image is null || this.key != key)
        {
            this.Rebuild(key, renderContent);
        }

        target.DrawImage(this.image, destination);
    }

    /// <summary>
    /// Drops the cached image.
    /// </summary>
    public void Clear()
    {
        this.image?.Dispose();
        this.image = null;
        this.hasKey = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Clear();
    }

    private void Rebuild(TitleBarInsetCacheKey newKey, Action<SKCanvas> renderContent)
    {
        var imageInfo = new SKImageInfo(
            newKey.PixelWidth,
            newKey.PixelHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var sourceSurface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Unable to create the title-bar inset source surface.");
        SKCanvas sourceCanvas = sourceSurface.Canvas;
        sourceCanvas.Clear(SKColors.Transparent);
        sourceCanvas.Scale(newKey.ScaleX, newKey.ScaleY);
        renderContent(sourceCanvas);
        sourceCanvas.Flush();

        using var sourceImage = sourceSurface.Snapshot();
        using var blurredSurface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Unable to create the title-bar inset blur surface.");
        SKCanvas blurredCanvas = blurredSurface.Canvas;
        blurredCanvas.Clear(SKColors.Transparent);
        using var blurFilter = SKImageFilter.CreateBlur(
            this.blurSigma * newKey.ScaleX,
            this.blurSigma * newKey.ScaleY);
        using var blurPaint = new SKPaint { ImageFilter = blurFilter };
        blurredCanvas.DrawImage(sourceImage, 0, 0, blurPaint);
        blurredCanvas.Flush();

        SKImage newImage = blurredSurface.Snapshot();
        this.image?.Dispose();
        this.image = newImage;
        this.key = newKey;
        this.hasKey = true;
        this.BuildCount++;
        RenderDiagnostics.RecordInsetRebuild();
    }
}
