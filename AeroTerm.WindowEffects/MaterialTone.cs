// <copyright file="MaterialTone.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

/// <summary>
/// Selects between the light and dark tonal variants of the platform
/// blur / acrylic / mica / vibrancy backdrop. Applied independently of
/// Avalonia's <c>Window.RequestedThemeVariant</c>, so the user can keep
/// chrome controls in one tone while the OS material renders in the
/// other.
/// </summary>
/// <remarks>
/// Has no effect when <see cref="BlurType.Transparent"/> is selected
/// (no material to tint) or on Linux (no portable hook into the
/// compositor's vibrancy material).
/// </remarks>
public enum MaterialTone
{
    /// <summary>
    /// Render the platform material in its light tonal variant
    /// (Windows: <c>DWMWA_USE_IMMERSIVE_DARK_MODE = 0</c>; macOS:
    /// <c>NSAppearanceNameAqua</c>).
    /// </summary>
    Light,

    /// <summary>
    /// Render the platform material in its dark tonal variant
    /// (Windows: <c>DWMWA_USE_IMMERSIVE_DARK_MODE = 1</c>; macOS:
    /// <c>NSAppearanceNameDarkAqua</c>).
    /// </summary>
    Dark,
}
