// <copyright file="UnicodeWidth.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Globalization;

/// <summary>
/// Determines the display width of Unicode characters for terminal grid layout.
/// Wide (double-width) characters occupy two cells; zero-width characters (combining
/// marks, ZWJ, variation selectors, format controls) occupy zero cells; everything
/// else occupies one cell.
/// </summary>
/// <remarks>
/// The East-Asian-Width ranges baked into this class correspond to the
/// Unicode Character Database version 15.1.0 (released 2023-09-12). Only
/// code points classified as <c>W</c> (Wide) or <c>F</c> (Fullwidth) in
/// <c>EastAsianWidth.txt</c> are treated as double-width; ambiguous
/// (<c>A</c>) code points are rendered as single-width per convention.
/// Zero-width detection delegates to <see cref="CharUnicodeInfo"/> for
/// non-spacing / enclosing marks and format characters, which keeps the
/// combining-mark data in sync with the .NET runtime's Unicode tables.
/// </remarks>
public static class UnicodeWidth
{
    // Unicode 15.1.0 EastAsianWidth=W|F ranges, coalesced and sorted.
    // Source: https://www.unicode.org/Public/15.1.0/ucd/EastAsianWidth.txt
    // Generated from the canonical UCD file; see artifacts/EastAsianWidth-15.1.0.txt.
    private static readonly (int Start, int End)[] WideRanges = new (int, int)[]
    {
        (0x1100, 0x115F),
        (0x231A, 0x231B),
        (0x2329, 0x232A),
        (0x23E9, 0x23EC),
        (0x23F0, 0x23F0),
        (0x23F3, 0x23F3),
        (0x25FD, 0x25FE),
        (0x2614, 0x2615),
        (0x2648, 0x2653),
        (0x267F, 0x267F),
        (0x2693, 0x2693),
        (0x26A1, 0x26A1),
        (0x26AA, 0x26AB),
        (0x26BD, 0x26BE),
        (0x26C4, 0x26C5),
        (0x26CE, 0x26CE),
        (0x26D4, 0x26D4),
        (0x26EA, 0x26EA),
        (0x26F2, 0x26F3),
        (0x26F5, 0x26F5),
        (0x26FA, 0x26FA),
        (0x26FD, 0x26FD),
        (0x2705, 0x2705),
        (0x270A, 0x270B),
        (0x2728, 0x2728),
        (0x274C, 0x274C),
        (0x274E, 0x274E),
        (0x2753, 0x2755),
        (0x2757, 0x2757),
        (0x2795, 0x2797),
        (0x27B0, 0x27B0),
        (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50),
        (0x2B55, 0x2B55),
        (0x2E80, 0x2E99),
        (0x2E9B, 0x2EF3),
        (0x2F00, 0x2FD5),
        (0x2FF0, 0x303E),
        (0x3041, 0x3096),
        (0x3099, 0x30FF),
        (0x3105, 0x312F),
        (0x3131, 0x318E),
        (0x3190, 0x31E3),
        (0x31EF, 0x321E),
        (0x3220, 0x3247),
        (0x3250, 0x4DBF),
        (0x4E00, 0xA48C),
        (0xA490, 0xA4C6),
        (0xA960, 0xA97C),
        (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF),
        (0xFE10, 0xFE19),
        (0xFE30, 0xFE52),
        (0xFE54, 0xFE66),
        (0xFE68, 0xFE6B),
        (0xFF01, 0xFF60),
        (0xFFE0, 0xFFE6),
        (0x16FE0, 0x16FE4),
        (0x16FF0, 0x16FF1),
        (0x17000, 0x187F7),
        (0x18800, 0x18CD5),
        (0x18D00, 0x18D08),
        (0x1AFF0, 0x1AFF3),
        (0x1AFF5, 0x1AFFB),
        (0x1AFFD, 0x1AFFE),
        (0x1B000, 0x1B122),
        (0x1B132, 0x1B132),
        (0x1B150, 0x1B152),
        (0x1B155, 0x1B155),
        (0x1B164, 0x1B167),
        (0x1B170, 0x1B2FB),
        (0x1F004, 0x1F004),
        (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F200, 0x1F202),
        (0x1F210, 0x1F23B),
        (0x1F240, 0x1F248),
        (0x1F250, 0x1F251),
        (0x1F260, 0x1F265),
        (0x1F300, 0x1F320),
        (0x1F32D, 0x1F335),
        (0x1F337, 0x1F37C),
        (0x1F37E, 0x1F393),
        (0x1F3A0, 0x1F3CA),
        (0x1F3CF, 0x1F3D3),
        (0x1F3E0, 0x1F3F0),
        (0x1F3F4, 0x1F3F4),
        (0x1F3F8, 0x1F43E),
        (0x1F440, 0x1F440),
        (0x1F442, 0x1F4FC),
        (0x1F4FF, 0x1F53D),
        (0x1F54B, 0x1F54E),
        (0x1F550, 0x1F567),
        (0x1F57A, 0x1F57A),
        (0x1F595, 0x1F596),
        (0x1F5A4, 0x1F5A4),
        (0x1F5FB, 0x1F64F),
        (0x1F680, 0x1F6C5),
        (0x1F6CC, 0x1F6CC),
        (0x1F6D0, 0x1F6D2),
        (0x1F6D5, 0x1F6D7),
        (0x1F6DC, 0x1F6DF),
        (0x1F6EB, 0x1F6EC),
        (0x1F6F4, 0x1F6FC),
        (0x1F7E0, 0x1F7EB),
        (0x1F7F0, 0x1F7F0),
        (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945),
        (0x1F947, 0x1F9FF),
        (0x1FA70, 0x1FA7C),
        (0x1FA80, 0x1FA88),
        (0x1FA90, 0x1FABD),
        (0x1FABF, 0x1FAC5),
        (0x1FACE, 0x1FADB),
        (0x1FAE0, 0x1FAE8),
        (0x1FAF0, 0x1FAF8),
        (0x20000, 0x2FFFD),
        (0x30000, 0x3FFFD),
    };

    /// <summary>
    /// Returns true if the given Unicode code point is a wide (double-width) character
    /// that occupies two cells in a terminal grid, per Unicode 15.1 East-Asian-Width
    /// property values W (Wide) and F (Fullwidth).
    /// </summary>
    /// <param name="codePoint">A Unicode code point.</param>
    /// <returns>True if the character is double-width.</returns>
    public static bool IsWideCharacter(int codePoint)
    {
        if (codePoint < WideRanges[0].Start)
        {
            return false;
        }

        int lo = 0;
        int hi = WideRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            var range = WideRanges[mid];
            if (codePoint < range.Start)
            {
                hi = mid - 1;
            }
            else if (codePoint > range.End)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the number of terminal cells occupied by the given Unicode code point:
    /// 0 for combining marks, format characters (including ZWJ), variation selectors
    /// and C0/C1 controls; 2 for East-Asian Wide/Fullwidth characters; 1 otherwise.
    /// </summary>
    /// <param name="codePoint">A Unicode code point (0x0..0x10FFFF).</param>
    /// <returns>0, 1, or 2.</returns>
    /// <remarks>
    /// Ambiguous-width (EAW=A) code points are reported as width 1. Controls are
    /// reported as width 0 so that callers which fall through to this method for
    /// unexpected input won't advance the cursor; the normal code path in the
    /// terminal handles C0/C1 as side-effects before reaching the buffer.
    /// </remarks>
    public static int GetWidth(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
        {
            return 1;
        }

        // C0 / DEL / C1 controls render as no cells.
        if (codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0))
        {
            return 0;
        }

        // Fast path: the bulk of Latin-1 printable characters.
        if (codePoint < 0x300)
        {
            return 1;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        if (category == UnicodeCategory.NonSpacingMark ||
            category == UnicodeCategory.EnclosingMark ||
            category == UnicodeCategory.Format)
        {
            // Soft hyphen is the one Cf code point traditionally rendered; terminals
            // generally don't, so we follow wcwidth convention and treat it as 0.
            return 0;
        }

        return IsWideCharacter(codePoint) ? 2 : 1;
    }
}
