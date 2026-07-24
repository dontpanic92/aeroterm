// <copyright file="TerminalRowCache.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using SkiaSharp;

/// <summary>
/// Retains vector background and foreground pictures per terminal row and
/// re-records only rows whose visual generation changed.
/// </summary>
internal sealed class TerminalRowCache : IDisposable
{
    private SKPicture?[] backgrounds = Array.Empty<SKPicture?>();
    private SKPicture?[] foregrounds = Array.Empty<SKPicture?>();
    private long[] rowGenerations = Array.Empty<long>();
    private TerminalRowCacheKey key;
    private bool hasKey;

    /// <summary>
    /// Gets the total number of row picture pairs recorded.
    /// Exposed for tests and diagnostics.
    /// </summary>
    internal int BuildCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a complete frame exists for the supplied key.
    /// </summary>
    /// <param name="key">Current static frame key.</param>
    /// <param name="rowCount">Current screen row count.</param>
    /// <returns><see langword="true"/> when every row has cached pictures.</returns>
    public bool HasCompleteFrame(TerminalRowCacheKey key, int rowCount)
    {
        if (!this.hasKey || this.key != key || this.backgrounds.Length != rowCount)
        {
            return false;
        }

        for (int row = 0; row < rowCount; row++)
        {
            if (this.backgrounds[row] is null || this.foregrounds[row] is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Counts rows that need re-recording for the current generations.
    /// </summary>
    /// <param name="key">Current static frame key.</param>
    /// <param name="generations">Current non-consumptive row generations.</param>
    /// <returns>The number of rows requiring new pictures.</returns>
    public int CountDirtyRows(TerminalRowCacheKey key, IReadOnlyList<long> generations)
    {
        if (!this.hasKey || this.key != key || this.backgrounds.Length != generations.Count)
        {
            return generations.Count;
        }

        int dirtyCount = 0;
        for (int row = 0; row < generations.Count; row++)
        {
            if (this.backgrounds[row] is null
                || this.foregrounds[row] is null
                || this.rowGenerations[row] != generations[row])
            {
                dirtyCount++;
            }
        }

        return dirtyCount;
    }

    /// <summary>
    /// Re-records changed row pictures.
    /// </summary>
    /// <param name="key">Current static frame key.</param>
    /// <param name="generations">Current non-consumptive row generations.</param>
    /// <param name="width">Logical terminal width.</param>
    /// <param name="lineHeight">Logical row height.</param>
    /// <param name="renderBackground">Records one row's background layer.</param>
    /// <param name="renderForeground">Records one row's foreground layer.</param>
    public void Update(
        TerminalRowCacheKey key,
        IReadOnlyList<long> generations,
        float width,
        float lineHeight,
        Action<SKCanvas, int> renderBackground,
        Action<SKCanvas, int> renderForeground)
    {
        ArgumentNullException.ThrowIfNull(renderBackground);
        ArgumentNullException.ThrowIfNull(renderForeground);

        if (generations.Count == 0 || width <= 0 || lineHeight <= 0)
        {
            this.Clear();
            return;
        }

        this.EnsureStorage(key, generations.Count);
        for (int row = 0; row < generations.Count; row++)
        {
            if (this.backgrounds[row] is not null
                && this.foregrounds[row] is not null
                && this.rowGenerations[row] == generations[row])
            {
                continue;
            }

            this.RebuildRow(
                row,
                width,
                lineHeight,
                generations[row],
                renderBackground,
                renderForeground);
        }
    }

    /// <summary>
    /// Draws all cached background row pictures.
    /// </summary>
    /// <param name="target">Target frame canvas.</param>
    /// <param name="topInset">Logical terminal top inset.</param>
    /// <param name="lineHeight">Logical row height.</param>
    public void DrawBackgrounds(SKCanvas target, float topInset, float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(target);
        this.DrawPictures(target, this.backgrounds, topInset, lineHeight);
    }

    /// <summary>
    /// Draws all cached foreground row pictures.
    /// </summary>
    /// <param name="target">Target frame canvas.</param>
    /// <param name="topInset">Logical terminal top inset.</param>
    /// <param name="lineHeight">Logical row height.</param>
    public void DrawForegrounds(SKCanvas target, float topInset, float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(target);
        this.DrawPictures(target, this.foregrounds, topInset, lineHeight);
    }

    /// <summary>
    /// Drops all cached row resources.
    /// </summary>
    public void Clear()
    {
        DisposePictures(this.backgrounds);
        DisposePictures(this.foregrounds);
        this.backgrounds = Array.Empty<SKPicture?>();
        this.foregrounds = Array.Empty<SKPicture?>();
        this.rowGenerations = Array.Empty<long>();
        this.hasKey = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Clear();
    }

    private static void DisposePictures(IEnumerable<SKPicture?> pictures)
    {
        foreach (SKPicture? picture in pictures)
        {
            picture?.Dispose();
        }
    }

    private void DrawPictures(
        SKCanvas target,
        IReadOnlyList<SKPicture?> pictures,
        float topInset,
        float lineHeight)
    {
        for (int row = 0; row < pictures.Count; row++)
        {
            SKPicture? picture = pictures[row];
            if (picture is null)
            {
                continue;
            }

            target.Save();
            target.Translate(0, topInset + (row * lineHeight));
            target.DrawPicture(picture);
            target.Restore();
        }
    }

    private void EnsureStorage(TerminalRowCacheKey newKey, int rowCount)
    {
        if (this.hasKey && this.key == newKey && this.backgrounds.Length == rowCount)
        {
            return;
        }

        this.Clear();
        this.backgrounds = new SKPicture?[rowCount];
        this.foregrounds = new SKPicture?[rowCount];
        this.rowGenerations = new long[rowCount];
        this.key = newKey;
        this.hasKey = true;
    }

    private void RebuildRow(
        int row,
        float width,
        float lineHeight,
        long generation,
        Action<SKCanvas, int> renderBackground,
        Action<SKCanvas, int> renderForeground)
    {
        this.backgrounds[row]?.Dispose();
        this.foregrounds[row]?.Dispose();

        using (var recorder = new SKPictureRecorder())
        {
            SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0, width, lineHeight));
            renderBackground(canvas, row);
            this.backgrounds[row] = recorder.EndRecording();
        }

        using (var recorder = new SKPictureRecorder())
        {
            // Allow glyphs and decorations to extend into adjacent rows just
            // as they do in the direct full-frame renderer.
            SKCanvas canvas = recorder.BeginRecording(
                new SKRect(0, -lineHeight, width, lineHeight * 3));
            renderForeground(canvas, row);
            this.foregrounds[row] = recorder.EndRecording();
        }

        this.rowGenerations[row] = generation;
        this.BuildCount++;
    }
}
