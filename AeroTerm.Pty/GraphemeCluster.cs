// <copyright file="GraphemeCluster.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Helpers for pragmatic UAX #29 grapheme-cluster segmentation suitable for
/// terminal cell layout. The full standard (legacy indic sequences, linking
/// consonants, etc.) is not implemented — this module handles the categories
/// that matter for terminal display: combining marks, ZWJ emoji sequences,
/// variation selectors, emoji skin-tone modifiers, regional-indicator flags,
/// keycap sequences, and Unicode tag sequences.
/// </summary>
internal static class GraphemeCluster
{
    /// <summary>
    /// Returns <see langword="true"/> if the code point is a regional-indicator
    /// symbol (U+1F1E6..U+1F1FF) used to form flag sequences.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to test.</param>
    /// <returns>Whether the code point is a regional-indicator symbol.</returns>
    public static bool IsRegionalIndicator(int codePoint) =>
        codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;

    /// <summary>
    /// Returns <see langword="true"/> if the code point is an emoji modifier
    /// (Fitzpatrick skin-tone selector, U+1F3FB..U+1F3FF).
    /// </summary>
    /// <param name="codePoint">The Unicode code point to test.</param>
    /// <returns>Whether the code point is an emoji modifier.</returns>
    public static bool IsEmojiModifier(int codePoint) =>
        codePoint >= 0x1F3FB && codePoint <= 0x1F3FF;

    /// <summary>
    /// Returns <see langword="true"/> if the code point is a variation selector
    /// (VS1..VS16 at U+FE00..U+FE0F or the supplementary range U+E0100..U+E01EF).
    /// </summary>
    /// <param name="codePoint">The Unicode code point to test.</param>
    /// <returns>Whether the code point is a variation selector.</returns>
    public static bool IsVariationSelector(int codePoint) =>
        (codePoint >= 0xFE00 && codePoint <= 0xFE0F) ||
        (codePoint >= 0xE0100 && codePoint <= 0xE01EF);

    /// <summary>
    /// Returns <see langword="true"/> if the code point is a Unicode tag
    /// character used in tag sequences (U+E0020..U+E007F).
    /// </summary>
    /// <param name="codePoint">The Unicode code point to test.</param>
    /// <returns>Whether the code point is a tag character.</returns>
    public static bool IsTagCharacter(int codePoint) =>
        codePoint >= 0xE0020 && codePoint <= 0xE007F;

    /// <summary>
    /// Decides whether <paramref name="codePoint"/> should extend the cluster
    /// whose code points are in <paramref name="pending"/> rather than start a
    /// new cluster. Implements a pragmatic subset of UAX #29 sufficient for
    /// terminal use: combining marks, ZWJ emoji sequences, variation selectors,
    /// keycap sequences, skin-tone modifiers, tag sequences and paired
    /// regional indicators (flags).
    /// </summary>
    /// <param name="pending">Code points already in the pending cluster.</param>
    /// <param name="codePoint">The candidate extending code point.</param>
    /// <returns>Whether the code point extends the pending cluster.</returns>
    public static bool ShouldExtend(IReadOnlyList<int> pending, int codePoint)
    {
        if (pending.Count == 0)
        {
            return false;
        }

        int last = pending[pending.Count - 1];

        // ZWJ and variation selectors always extend.
        if (codePoint == 0x200D || IsVariationSelector(codePoint))
        {
            return true;
        }

        // Tag characters extend (tag sequences like subdivision flags).
        if (IsTagCharacter(codePoint))
        {
            return true;
        }

        // Combining marks (non-spacing, spacing-combining, enclosing) extend.
        // Guard against surrogate-only inputs; GetUnicodeCategory handles
        // full code points up to U+10FFFF.
        if ((uint)codePoint <= 0x10FFFF)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
            if (category == UnicodeCategory.NonSpacingMark ||
                category == UnicodeCategory.SpacingCombiningMark ||
                category == UnicodeCategory.EnclosingMark)
            {
                return true;
            }
        }

        // Emoji modifier extends an emoji base.
        if (IsEmojiModifier(codePoint))
        {
            return true;
        }

        // After ZWJ, any emoji-ish (wide) code point extends the cluster.
        if (last == 0x200D && (UnicodeWidth.IsWideCharacter(codePoint) || IsRegionalIndicator(codePoint)))
        {
            return true;
        }

        // Pair consecutive regional indicators into flag sequences: a new RI
        // extends the cluster iff the trailing run of RIs has odd length
        // (forming an even pair). If the trailing run already has even length
        // we start a new flag cluster.
        if (IsRegionalIndicator(codePoint) && IsRegionalIndicator(last))
        {
            int trailing = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (IsRegionalIndicator(pending[i]))
                {
                    trailing++;
                }
                else
                {
                    break;
                }
            }

            return (trailing & 1) == 1;
        }

        return false;
    }

    /// <summary>
    /// Computes the display width (1 or 2 terminal cells) of a grapheme
    /// cluster expressed as its constituent code points. A cluster is wide if
    /// any code point is East-Asian Wide/Fullwidth, if it is a regional-
    /// indicator flag pair, if it contains an emoji modifier, or if it
    /// contains the emoji-presentation variation selector VS16 (U+FE0F).
    /// VS15 (U+FE0E) suppresses emoji presentation and leaves width at 1.
    /// </summary>
    /// <param name="codePoints">The code points composing the cluster.</param>
    /// <returns>1 or 2 terminal cells.</returns>
    public static int ComputeWidth(IReadOnlyList<int> codePoints)
    {
        if (codePoints.Count == 0)
        {
            return 1;
        }

        bool hasVs16 = false;
        bool hasVs15 = false;
        bool hasRegionalIndicator = false;
        bool hasEmojiModifier = false;
        bool anyWide = false;

        for (int i = 0; i < codePoints.Count; i++)
        {
            int cp = codePoints[i];
            if (cp == 0xFE0F)
            {
                hasVs16 = true;
            }
            else if (cp == 0xFE0E)
            {
                hasVs15 = true;
            }
            else if (IsRegionalIndicator(cp))
            {
                hasRegionalIndicator = true;
            }
            else if (IsEmojiModifier(cp))
            {
                hasEmojiModifier = true;
            }
            else if (UnicodeWidth.IsWideCharacter(cp))
            {
                anyWide = true;
            }
        }

        if (anyWide || hasVs16 || hasEmojiModifier || hasRegionalIndicator)
        {
            return 2;
        }

        _ = hasVs15;
        return 1;
    }
}
