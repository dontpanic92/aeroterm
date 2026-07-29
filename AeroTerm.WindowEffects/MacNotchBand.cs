// <copyright file="MacNotchBand.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

/// <summary>
/// Geometry of the strip left unused above a macOS native full-screen
/// window on a display with a camera housing ("notch").
/// <para>
/// AppKit clamps native full-screen windows to the safe area, leaving a
/// black band across the top of the display. The band cannot be reclaimed
/// by the full-screen window itself, but a floating auxiliary window may
/// draw into it. All values are in points, with X measured from the left
/// edge of the hosting screen.
/// </para>
/// </summary>
/// <param name="Height">Height of the unused band above the full-screen window.</param>
/// <param name="ScreenWidth">Width of the hosting screen.</param>
/// <param name="NotchLeft">
/// X coordinate where the camera housing starts, i.e. the width of the
/// usable area to its left.
/// </param>
/// <param name="NotchRight">
/// X coordinate where the camera housing ends and the usable area to its
/// right begins.
/// </param>
/// <param name="ScreenTopY">
/// Y coordinate of the top edge of the hosting screen in AppKit's global
/// (bottom-left origin) coordinate space, used to measure how far the
/// pointer currently sits from the top of that screen.
/// </param>
/// <param name="ScreenLeftX">
/// X coordinate of the left edge of the hosting screen in AppKit's global
/// coordinate space.
/// </param>
public readonly record struct MacNotchBand(
    double Height,
    double ScreenWidth,
    double NotchLeft,
    double NotchRight,
    double ScreenTopY,
    double ScreenLeftX);
