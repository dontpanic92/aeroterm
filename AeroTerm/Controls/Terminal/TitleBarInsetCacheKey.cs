// <copyright file="TitleBarInsetCacheKey.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Identifies all inputs that affect a cached title-bar inset image.
/// </summary>
/// <param name="PixelWidth">Cached image width in physical pixels.</param>
/// <param name="PixelHeight">Cached image height in physical pixels.</param>
/// <param name="ScaleX">Horizontal device scale.</param>
/// <param name="ScaleY">Vertical device scale.</param>
/// <param name="ScrollbackCount">Current number of retained scrollback rows.</param>
/// <param name="ScrollbackEvictedTotal">Total scrollback rows evicted from the ring.</param>
/// <param name="ViewportOffset">Current viewport offset.</param>
/// <param name="FontSize">Current Skia font size.</param>
/// <param name="TypefaceHandle">Current primary typeface handle.</param>
/// <param name="DefaultForeground">Current default foreground color.</param>
/// <param name="DefaultBackground">Current default background color.</param>
/// <param name="PaletteIdentity">Identity of the immutable palette array.</param>
internal readonly record struct TitleBarInsetCacheKey(
    int PixelWidth,
    int PixelHeight,
    float ScaleX,
    float ScaleY,
    int ScrollbackCount,
    long ScrollbackEvictedTotal,
    int ViewportOffset,
    float FontSize,
    nint TypefaceHandle,
    int DefaultForeground,
    int DefaultBackground,
    int PaletteIdentity);
