// <copyright file="ColorRef.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Tagged-int encoding for "logical" cell colors. A cell's foreground or
/// background color is no longer a baked RGB: it can be a literal RGB,
/// a reference to a palette index, or one of the default-color sentinels.
/// Cells store these logical values directly; resolution to a paintable
/// RGB happens at render time via <see cref="PaletteSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Layout (32-bit signed int):
/// </para>
/// <list type="bullet">
///   <item><description><c>0x00_RR_GG_BB</c> — RGB literal (the historical
///     encoding; truecolor SGR <c>38;2;R;G;B</c> emits this form).</description></item>
///   <item><description><c>0x01_00_00_NN</c> — palette index reference for
///     <c>NN ∈ [0,255]</c>. Resolved through
///     <see cref="TerminalBuffer.GetPaletteColor"/>.</description></item>
///   <item><description><c>0x02_00_00_00</c> — default foreground sentinel
///     (<see cref="DefaultFg"/>). Resolves to the buffer's current
///     <see cref="TerminalBuffer.DefaultForeground"/>.</description></item>
///   <item><description><c>0x03_00_00_00</c> — default background sentinel
///     (<see cref="DefaultBg"/>). Resolves to the buffer's current
///     <see cref="TerminalBuffer.DefaultBackground"/>.</description></item>
/// </list>
/// <para>
/// Truecolor literals are unaffected by palette / scheme changes (per VT
/// specification). Palette-ref and default-sentinel cells automatically
/// repaint with the new scheme — including cells that already sit in the
/// scrollback ring — because resolution happens at render time.
/// </para>
/// </remarks>
public static class ColorRef
{
    /// <summary>
    /// Sentinel value indicating "use the buffer's current default
    /// foreground color" (the user's color-scheme foreground).
    /// </summary>
    public const int DefaultFg = 0x02_00_00_00;

    /// <summary>
    /// Sentinel value indicating "use the buffer's current default
    /// background color" (the user's color-scheme background).
    /// </summary>
    public const int DefaultBg = 0x03_00_00_00;

    private const int RgbMask = 0x00_FF_FF_FF;
    private const int TagMask = unchecked((int)0xFF_00_00_00);
    private const int TagPalette = 0x01_00_00_00;

    /// <summary>
    /// Returns a palette-index logical color for the given 0..255 index.
    /// </summary>
    /// <param name="index">Palette index (clamped to <c>[0, 255]</c>).</param>
    /// <returns>An encoded palette-index logical color.</returns>
    public static int Palette(int index)
    {
        if (index < 0)
        {
            index = 0;
        }
        else if (index > 255)
        {
            index = 255;
        }

        return TagPalette | index;
    }

    /// <summary>
    /// Returns true if <paramref name="value"/> is an RGB literal.
    /// </summary>
    /// <param name="value">A logical color value.</param>
    /// <returns>True for RGB literals.</returns>
    public static bool IsRgb(int value) => (value & TagMask) == 0;

    /// <summary>
    /// Returns true if <paramref name="value"/> is a palette-index reference.
    /// </summary>
    /// <param name="value">A logical color value.</param>
    /// <returns>True for palette references.</returns>
    public static bool IsPalette(int value) => (value & TagMask) == TagPalette;

    /// <summary>
    /// Returns true if <paramref name="value"/> is the default-foreground
    /// sentinel.
    /// </summary>
    /// <param name="value">A logical color value.</param>
    /// <returns>True for the default-fg sentinel.</returns>
    public static bool IsDefaultFg(int value) => value == DefaultFg;

    /// <summary>
    /// Returns true if <paramref name="value"/> is the default-background
    /// sentinel.
    /// </summary>
    /// <param name="value">A logical color value.</param>
    /// <returns>True for the default-bg sentinel.</returns>
    public static bool IsDefaultBg(int value) => value == DefaultBg;

    /// <summary>
    /// Returns the palette index encoded in <paramref name="value"/>.
    /// Caller must have already verified <see cref="IsPalette"/>.
    /// </summary>
    /// <param name="value">A palette-encoded logical color.</param>
    /// <returns>The 0..255 palette index.</returns>
    public static int PaletteIndex(int value) => value & 0xFF;

    /// <summary>
    /// Returns the RGB triple encoded in <paramref name="value"/>. Caller
    /// must have already verified <see cref="IsRgb"/>.
    /// </summary>
    /// <param name="value">An RGB-literal logical color.</param>
    /// <returns>The 24-bit RGB value.</returns>
    public static int RgbValue(int value) => value & RgbMask;
}
