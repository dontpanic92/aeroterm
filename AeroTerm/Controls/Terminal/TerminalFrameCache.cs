// <copyright file="TerminalFrameCache.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Caches the terminal frame without cursor or IME overlays as a raster image.
/// </summary>
internal sealed class TerminalFrameCache : IDisposable
{
    private SKSurface? surface;
    private SKImage? image;
    private TerminalFrameCacheKey key;
    private bool hasKey;

    /// <summary>
    /// Gets the number of cached frame builds. Exposed for tests and diagnostics.
    /// </summary>
    internal int BuildCount { get; private set; }

    /// <summary>
    /// Attempts to draw an existing cached frame.
    /// </summary>
    /// <param name="target">Target frame canvas.</param>
    /// <param name="destination">Destination rectangle in target coordinates.</param>
    /// <param name="key">Complete cache dependency key.</param>
    /// <returns><see langword="true"/> when a matching frame was drawn.</returns>
    public bool TryDraw(
        SKCanvas target,
        SKRect destination,
        TerminalFrameCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!this.hasKey || this.image is null || this.key != key)
        {
            return false;
        }

        this.DrawImage(target, destination, key);
        return true;
    }

    /// <summary>
    /// Rebuilds and draws the cached static frame.
    /// </summary>
    /// <param name="target">Target frame canvas.</param>
    /// <param name="destination">Destination rectangle in target coordinates.</param>
    /// <param name="key">Complete cache dependency key.</param>
    /// <param name="renderFrame">Draws the static frame in device-independent coordinates.</param>
    public void RebuildAndDraw(
        SKCanvas target,
        SKRect destination,
        TerminalFrameCacheKey key,
        Action<SKCanvas> renderFrame)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(renderFrame);

        if (key.PixelWidth <= 0 || key.PixelHeight <= 0)
        {
            this.Clear();
            return;
        }

        bool dimensionsChanged = this.surface is null
            || this.key.PixelWidth != key.PixelWidth
            || this.key.PixelHeight != key.PixelHeight;
        this.Rebuild(key, dimensionsChanged, renderFrame);
        this.DrawImage(target, destination, key);
    }

    /// <summary>
    /// Drops all cached raster resources.
    /// </summary>
    public void Clear()
    {
        this.image?.Dispose();
        this.image = null;
        this.surface?.Dispose();
        this.surface = null;
        this.hasKey = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Clear();
    }

    private void Rebuild(TerminalFrameCacheKey newKey, bool dimensionsChanged, Action<SKCanvas> renderFrame)
    {
        this.image?.Dispose();
        this.image = null;

        if (dimensionsChanged)
        {
            this.surface?.Dispose();
            this.surface = SKSurface.Create(new SKImageInfo(
                newKey.PixelWidth,
                newKey.PixelHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Unable to create the terminal frame cache surface.");
        }

        SKCanvas cacheCanvas = this.surface!.Canvas;
        cacheCanvas.ResetMatrix();
        cacheCanvas.Clear(SKColors.Transparent);
        cacheCanvas.Scale(newKey.ScaleX, newKey.ScaleY);
        renderFrame(cacheCanvas);
        cacheCanvas.Flush();

        this.image = this.surface.Snapshot();
        this.key = newKey;
        this.hasKey = true;
        this.BuildCount++;
    }

    private void DrawImage(SKCanvas target, SKRect destination, TerminalFrameCacheKey imageKey)
    {
        var source = new SKRect(
            0,
            0,
            destination.Width * imageKey.ScaleX,
            destination.Height * imageKey.ScaleY);
        target.DrawImage(this.image, source, destination);
    }
}
