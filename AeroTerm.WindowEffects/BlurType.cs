// <copyright file="BlurType.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

/// <summary>
/// Specifies the window transparency blur effect type.
/// </summary>
public enum BlurType
{
    /// <summary>
    /// Gaussian blur (Windows 10 only).
    /// </summary>
    Gaussian = 0,

    /// <summary>
    /// Acrylic blur effect.
    /// </summary>
    Acrylic = 1,

    /// <summary>
    /// Mica effect (Windows 11 22H2+).
    /// </summary>
    Mica = 2,

    /// <summary>
    /// Plain transparent background without blur.
    /// </summary>
    Transparent = 3,

    /// <summary>
    /// macOS Liquid Glass effect (macOS 26+). Falls back to
    /// <see cref="Transparent"/> on older macOS versions and to
    /// <see cref="Acrylic"/> on non-macOS platforms.
    /// </summary>
    LiquidGlass = 4,
}
