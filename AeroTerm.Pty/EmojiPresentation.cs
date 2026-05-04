// <copyright file="EmojiPresentation.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Globalization;

/// <summary>
/// Helpers for detecting text that should prefer emoji presentation.
/// </summary>
public static class EmojiPresentation
{
    private const int ZeroWidthJoiner = 0x200D;
    private const int VariationSelector15 = 0xFE0E;
    private const int VariationSelector16 = 0xFE0F;

    /// <summary>
    /// Returns a value indicating whether <paramref name="text"/> contains an
    /// emoji-presentation text element.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns><see langword="true"/> when an emoji-presentation element is present.</returns>
    public static bool ContainsEmojiPresentation(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            if (IsEmojiPresentationElement(enumerator.GetTextElement()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a value indicating whether a single text element should be
    /// drawn using emoji presentation.
    /// </summary>
    /// <param name="textElement">The text element to inspect.</param>
    /// <returns><see langword="true"/> when the element should use emoji presentation.</returns>
    public static bool IsEmojiPresentationElement(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return false;
        }

        bool hasEmojiBase = false;
        bool hasEmojiPresentationHint = false;
        bool hasTextPresentationHint = false;

        foreach (var rune in textElement.EnumerateRunes())
        {
            int value = rune.Value;
            if (value == VariationSelector15)
            {
                hasTextPresentationHint = true;
                continue;
            }

            if (value == VariationSelector16
                || value == ZeroWidthJoiner
                || GraphemeCluster.IsEmojiModifier(value)
                || GraphemeCluster.IsRegionalIndicator(value))
            {
                hasEmojiPresentationHint = true;
                continue;
            }

            if (IsEmojiBaseCodePoint(value))
            {
                hasEmojiBase = true;
            }
        }

        return !hasTextPresentationHint && (hasEmojiBase || hasEmojiPresentationHint);
    }

    private static bool IsEmojiBaseCodePoint(int value)
    {
        return value is >= 0x1F000 and <= 0x1FAFF
            or >= 0x2600 and <= 0x27BF
            or 0x00A9
            or 0x00AE
            or 0x3030
            or 0x303D
            or 0x3297
            or 0x3299;
    }
}
