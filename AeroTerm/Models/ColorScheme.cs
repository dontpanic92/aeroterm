// <copyright file="ColorScheme.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Models;

/// <summary>
/// Defines a terminal color scheme with foreground, background, and 16-color ANSI palette.
/// </summary>
/// <param name="Name">Display name of the color scheme.</param>
/// <param name="Foreground">Default foreground color as 24-bit RGB integer.</param>
/// <param name="Background">Default background color as 24-bit RGB integer.</param>
/// <param name="Palette">16-color ANSI palette (indices 0-7 normal, 8-15 bright) as 24-bit RGB integers.</param>
public sealed record ColorScheme(string Name, int Foreground, int Background, int[] Palette)
{
    /// <summary>
    /// Gets the number of colors in the ANSI palette.
    /// </summary>
    public const int PaletteSize = 16;

    /// <summary>
    /// Gets the optional selection overlay color as a 24-bit RGB integer.
    /// When <see langword="null"/> the renderer derives a tint from the
    /// foreground color.
    /// </summary>
    public int? Selection { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.Name;
    }
}
