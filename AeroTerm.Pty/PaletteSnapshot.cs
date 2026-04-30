// <copyright file="PaletteSnapshot.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Immutable, point-in-time view of the per-frame data needed to resolve
/// a cell's logical color (see <see cref="ColorRef"/>) into a paintable
/// RGB. Captured by <see cref="TerminalBuffer"/> when a frame is produced
/// (see <see cref="Screen.Palette"/>) and consumed by the renderer.
/// </summary>
/// <remarks>
/// The palette array is shared by reference for performance — it must
/// never be mutated by consumers. <see cref="TerminalBuffer"/> only
/// publishes a fresh array when its internal palette mutates, so a
/// captured snapshot remains valid even if the buffer continues to run.
/// </remarks>
public readonly struct PaletteSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteSnapshot"/> struct.
    /// </summary>
    /// <param name="defaultForeground">The buffer's current default
    /// foreground RGB.</param>
    /// <param name="defaultBackground">The buffer's current default
    /// background RGB.</param>
    /// <param name="palette">The 256-entry palette array. Stored by
    /// reference; must not be mutated after construction.</param>
    public PaletteSnapshot(int defaultForeground, int defaultBackground, int[] palette)
    {
        this.DefaultForeground = defaultForeground;
        this.DefaultBackground = defaultBackground;
        this.Palette = palette;
    }

    /// <summary>
    /// Gets the default foreground RGB this snapshot was captured with.
    /// </summary>
    public int DefaultForeground { get; }

    /// <summary>
    /// Gets the default background RGB this snapshot was captured with.
    /// </summary>
    public int DefaultBackground { get; }

    /// <summary>
    /// Gets the 256-entry palette array (indices 0..255). Treat as
    /// read-only; do not mutate.
    /// </summary>
    public int[] Palette { get; }

    /// <summary>
    /// Resolves a logical foreground color to a paintable RGB.
    /// </summary>
    /// <param name="logical">A logical color encoded per <see cref="ColorRef"/>.</param>
    /// <returns>The resolved 24-bit RGB.</returns>
    public int ResolveForeground(int logical)
    {
        if (logical == ColorRef.DefaultFg)
        {
            return this.DefaultForeground;
        }

        if (logical == ColorRef.DefaultBg)
        {
            return this.DefaultBackground;
        }

        if (ColorRef.IsPalette(logical))
        {
            int idx = ColorRef.PaletteIndex(logical);
            int[] p = this.Palette;
            if (p is not null && (uint)idx < (uint)p.Length)
            {
                return p[idx];
            }

            return this.DefaultForeground;
        }

        return ColorRef.RgbValue(logical);
    }

    /// <summary>
    /// Resolves a logical background color to a paintable RGB.
    /// </summary>
    /// <param name="logical">A logical color encoded per <see cref="ColorRef"/>.</param>
    /// <returns>The resolved 24-bit RGB.</returns>
    public int ResolveBackground(int logical)
    {
        if (logical == ColorRef.DefaultBg)
        {
            return this.DefaultBackground;
        }

        if (logical == ColorRef.DefaultFg)
        {
            return this.DefaultForeground;
        }

        if (ColorRef.IsPalette(logical))
        {
            int idx = ColorRef.PaletteIndex(logical);
            int[] p = this.Palette;
            if (p is not null && (uint)idx < (uint)p.Length)
            {
                return p[idx];
            }

            return this.DefaultBackground;
        }

        return ColorRef.RgbValue(logical);
    }
}
