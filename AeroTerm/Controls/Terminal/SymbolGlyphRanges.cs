// <copyright file="SymbolGlyphRanges.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Fast classifier that decides whether a code point should be painted by
/// the programmatic <see cref="SymbolGlyphRenderer"/> instead of going
/// through the font path.
/// </summary>
/// <remarks>
/// The terminal renderer rounds cell width and line height up to whole
/// pixels for grid stability, but font glyphs draw at their natural
/// (unrounded) advance anchored at the cell origin. For symbols whose
/// strokes are designed to span the full cell (box drawing, block
/// elements, Powerline separators, Symbols for Legacy Computing) this
/// leaves a sub-pixel gap on the right and bottom of every cell, which
/// looks like disconnected lines or seams between tiles. Painting these
/// glyphs ourselves with primitives sized to the exact cell rect avoids
/// the gap regardless of font metrics.
/// </remarks>
internal static class SymbolGlyphRanges
{
    /// <summary>
    /// Returns <c>true</c> if the given code point should be rendered by
    /// <see cref="SymbolGlyphRenderer"/> rather than via a typeface.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to classify.</param>
    /// <returns><c>true</c> if the code point belongs to a supported
    /// programmatic-rendering range; otherwise <c>false</c>.</returns>
    public static bool Handles(int codePoint)
    {
        // Box Drawing + Block Elements: U+2500..U+259F.
        if ((uint)(codePoint - 0x2500) <= (0x259F - 0x2500))
        {
            return true;
        }

        // Powerline glyphs (Nerd Font PUA): U+E0A0..U+E0D4.
        if ((uint)(codePoint - 0xE0A0) <= (0xE0D4 - 0xE0A0))
        {
            return true;
        }

        // Symbols for Legacy Computing: U+1FB00..U+1FBFF.
        if ((uint)(codePoint - 0x1FB00) <= (0x1FBFF - 0x1FB00))
        {
            return true;
        }

        return false;
    }
}
