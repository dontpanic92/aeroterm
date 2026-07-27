// <copyright file="TitleBarInsetCacheKey.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Identifies all inputs that affect a cached title-bar inset image.
/// </summary>
/// <remarks>
/// The key deliberately does <em>not</em> include the scrollback count or the
/// viewport offset. Those change on every scrolled output line, which would
/// force a full CPU rasterization, Gaussian blur, and texture upload on every
/// terminal frame. Instead <paramref name="GhostContentHash"/> summarizes the
/// glyphs and resolved colors actually drawn into the inset, so the cache only
/// rebuilds when the visible ghost rows change.
/// </remarks>
/// <param name="PixelWidth">Cached image width in physical pixels.</param>
/// <param name="PixelHeight">Cached image height in physical pixels.</param>
/// <param name="ScaleX">Horizontal device scale.</param>
/// <param name="ScaleY">Vertical device scale.</param>
/// <param name="GhostRowCount">Number of scrollback ghost rows drawn.</param>
/// <param name="GhostContentHash">Hash of the ghost rows' glyphs and resolved foreground colors.</param>
/// <param name="FontSize">Current Skia font size.</param>
/// <param name="CharWidth">Current cell width in device-independent pixels.</param>
/// <param name="LineHeight">Current row height in device-independent pixels.</param>
/// <param name="TypefaceHandle">Current primary typeface handle.</param>
internal readonly record struct TitleBarInsetCacheKey(
    int PixelWidth,
    int PixelHeight,
    float ScaleX,
    float ScaleY,
    int GhostRowCount,
    int GhostContentHash,
    float FontSize,
    float CharWidth,
    float LineHeight,
    nint TypefaceHandle);
