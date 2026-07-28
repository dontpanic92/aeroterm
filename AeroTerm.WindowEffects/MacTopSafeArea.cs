// <copyright file="MacTopSafeArea.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

/// <summary>
/// Describes the unsafe strip along the top edge of a notched Mac display,
/// expressed in window-local points (left-to-right, top-left origin) so it
/// can be applied directly to an Avalonia layout.
/// </summary>
/// <param name="TopInset">
/// Height of the camera-housing band, i.e. <c>NSScreen.safeAreaInsets.top</c>.
/// </param>
/// <param name="NotchLeft">
/// X coordinate where the camera housing starts — equivalently the width of
/// the usable area to the left of the notch.
/// </param>
/// <param name="NotchRight">
/// X coordinate where the camera housing ends and the usable area to the
/// right of the notch begins.
/// </param>
public readonly record struct MacTopSafeArea(double TopInset, double NotchLeft, double NotchRight);
