// <copyright file="UnicodeWidth15_1Tests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Spot-checks for the Unicode 15.1 delta in <see cref="UnicodeWidth"/>:
/// newly-assigned emoji, CJK Unified Ideographs Extension I, the four new
/// Ideographic Description Characters, and zero-width classification for
/// combining / format additions introduced at or before Unicode 15.1.
/// </summary>
[TestFixture]
public class UnicodeWidth15_1Tests
{
    /// <summary>Unicode 15.1 emoji and symbol additions classified as EAW=W render as width 2.</summary>
    /// <param name="codePoint">A code point added in or re-classified by Unicode 15.1.</param>
    [TestCase(0x1FA89, TestName = "U+1FA89 Harp")]
    [TestCase(0x1FA8F, TestName = "U+1FA8F Shovel")]
    [TestCase(0x1FABD, TestName = "U+1FABD Wing")]
    [TestCase(0x1FABF, TestName = "U+1FABF Goose")]
    [TestCase(0x1FACE, TestName = "U+1FACE Moose")]
    [TestCase(0x1FACF, TestName = "U+1FACF Donkey")]
    [TestCase(0x1F6DC, TestName = "U+1F6DC Wireless")]
    public void Unicode151EmojiAdditionsAreWide(int codePoint)
    {
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.True, $"U+{codePoint:X4} should be wide");
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(2));
    }

    /// <summary>The four Ideographic Description Characters added in Unicode 15.1 are wide.</summary>
    /// <param name="codePoint">U+2FFC..U+2FFF.</param>
    [TestCase(0x2FFC)]
    [TestCase(0x2FFD)]
    [TestCase(0x2FFE)]
    [TestCase(0x2FFF)]
    public void IdeographicDescriptionCharacters151AreWide(int codePoint)
    {
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(2));
    }

    /// <summary>CJK Unified Ideographs Extension I (U+2EBF0..U+2EE5D) spot-checks — wide.</summary>
    /// <param name="codePoint">A code point from the Extension I block.</param>
    [TestCase(0x2EBF0)]
    [TestCase(0x2EC00)]
    [TestCase(0x2ED00)]
    [TestCase(0x2EE5D)]
    public void CjkExtensionIIsWide(int codePoint)
    {
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.True, $"U+{codePoint:X} should be wide");
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(2));
    }

    /// <summary>The unassigned gap between the two Symbols Extended-A ranges stays narrow.</summary>
    /// <param name="codePoint">A code point in the 1FA8A..1FA8E reserved gap.</param>
    [TestCase(0x1FA8A)]
    [TestCase(0x1FA8E)]
    public void ReservedGapBetweenFifteenOneEmojiIsNotWide(int codePoint)
    {
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.False, $"U+{codePoint:X} is reserved and should not be wide");
    }

    /// <summary>Combining / format code points assigned by Unicode 15.1 resolve to zero width.</summary>
    [Test]
    public void CombiningAndFormatSpotChecks()
    {
        // U+0897 ARABIC PEPET was assigned in Unicode 15.0 (Mn) — still zero-width post-15.1.
        Assert.That(UnicodeWidth.GetWidth(0x0897), Is.EqualTo(0));

        // U+10EFD..U+10EFF ARABIC COMBINING MARKS added in Unicode 15.1 (Mn).
        Assert.That(UnicodeWidth.GetWidth(0x10EFD), Is.EqualTo(0));
        Assert.That(UnicodeWidth.GetWidth(0x10EFF), Is.EqualTo(0));

        // Variation Selector-16 (emoji presentation) is a Cf format character.
        Assert.That(UnicodeWidth.GetWidth(0xFE0F), Is.EqualTo(0));

        // Zero-width joiner, used by modern multi-codepoint emoji clusters.
        Assert.That(UnicodeWidth.GetWidth(0x200D), Is.EqualTo(0));
    }
}
