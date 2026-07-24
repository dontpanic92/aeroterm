// <copyright file="TerminalRowCacheKey.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Identifies all non-cell inputs that affect retained terminal row pictures.
/// </summary>
/// <param name="PixelWidth">Terminal width in physical pixels.</param>
/// <param name="PixelHeight">Terminal height in physical pixels.</param>
/// <param name="ScaleX">Horizontal device scale.</param>
/// <param name="ScaleY">Vertical device scale.</param>
/// <param name="Rows">Rendered row count.</param>
/// <param name="Columns">Rendered column count.</param>
/// <param name="TopInset">Terminal top inset.</param>
/// <param name="BackgroundAlpha">Default background alpha.</param>
/// <param name="EnableLigature">Whether ligature shaping is enabled.</param>
/// <param name="FontChainGeneration">Current font-chain generation.</param>
/// <param name="FontSize">Current Skia font size.</param>
/// <param name="CharWidth">Current terminal cell width.</param>
/// <param name="LineHeight">Current terminal line height.</param>
/// <param name="ViewportOffset">Current scrollback viewport offset.</param>
/// <param name="ScreenBackground">Current detected screen background.</param>
/// <param name="ScreenForeground">Current derived screen foreground.</param>
/// <param name="DefaultForeground">Current palette default foreground.</param>
/// <param name="DefaultBackground">Current palette default background.</param>
/// <param name="PaletteIdentity">Identity of the immutable palette array.</param>
internal readonly record struct TerminalRowCacheKey(
    int PixelWidth,
    int PixelHeight,
    float ScaleX,
    float ScaleY,
    int Rows,
    int Columns,
    float TopInset,
    byte BackgroundAlpha,
    bool EnableLigature,
    int FontChainGeneration,
    float FontSize,
    float CharWidth,
    float LineHeight,
    int ViewportOffset,
    int ScreenBackground,
    int ScreenForeground,
    int DefaultForeground,
    int DefaultBackground,
    int PaletteIdentity);
