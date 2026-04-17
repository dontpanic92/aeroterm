// <copyright file="UnicodeWidthTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="UnicodeWidth"/> covering the Unicode 15.1
/// East-Asian-Width ranges and zero-width classification.
/// </summary>
[TestFixture]
public class UnicodeWidthTests
{
    /// <summary>ASCII and Latin-1 code points render as single-width.</summary>
    /// <param name="codePoint">A code point expected to be narrow.</param>
    [TestCase(0x0041)]
    [TestCase(0x007E)]
    [TestCase(0x00A1)]
    [TestCase(0x00E9)]
    [TestCase(0x00FF)]
    [TestCase(0x0100)]
    public void NarrowAndLatinReturnOne(int codePoint)
    {
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(1));
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.False);
    }

    /// <summary>Canonical wide characters (CJK, kana, Hangul, emoji) report width 2.</summary>
    /// <param name="codePoint">A code point expected to be wide.</param>
    [TestCase(0x4E00)]
    [TestCase(0x3042)]
    [TestCase(0x30AB)]
    [TestCase(0xFF21)]
    [TestCase(0xAC00)]
    [TestCase(0x1F600)]
    [TestCase(0x1F680)]
    public void KnownWideCharactersReturnTwo(int codePoint)
    {
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.True);
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(2));
    }

    /// <summary>Combining marks, ZWJ, variation selectors and format controls are zero-width.</summary>
    /// <param name="codePoint">A code point expected to be zero-width.</param>
    [TestCase(0x0300)]
    [TestCase(0x034F)]
    [TestCase(0x200D)]
    [TestCase(0x200B)]
    [TestCase(0xFE0F)]
    [TestCase(0xE0100)]
    [TestCase(0x0897)]
    public void CombiningAndFormatReturnZero(int codePoint)
    {
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(0));
    }

    /// <summary>C0/C1/DEL controls are treated as zero-width cells.</summary>
    /// <param name="codePoint">A control code point.</param>
    [TestCase(0x0000)]
    [TestCase(0x001B)]
    [TestCase(0x007F)]
    [TestCase(0x0085)]
    public void ControlsReturnZero(int codePoint)
    {
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(0));
    }

    /// <summary>Unicode 15.0/15.1 additions classified as EAW=W render as wide.</summary>
    /// <param name="codePoint">A code point added in Unicode 15.0 or 15.1.</param>
    [TestCase(0x31350)]
    [TestCase(0x323AF)]
    [TestCase(0x1AFF0)]
    [TestCase(0x1B132)]
    [TestCase(0x1B155)]
    [TestCase(0x1FACE)]
    [TestCase(0x1FABF)]
    [TestCase(0x31EF)]
    public void Unicode151AdditionsAreWide(int codePoint)
    {
        Assert.That(UnicodeWidth.IsWideCharacter(codePoint), Is.True, $"U+{codePoint:X4} should be wide");
        Assert.That(UnicodeWidth.GetWidth(codePoint), Is.EqualTo(2));
    }

    /// <summary>Range boundaries are exact — one-off neighbours of wide blocks are not wide.</summary>
    [Test]
    public void BoundariesAreRespected()
    {
        Assert.That(UnicodeWidth.IsWideCharacter(0x10FF), Is.False);
        Assert.That(UnicodeWidth.IsWideCharacter(0x1100), Is.True);
        Assert.That(UnicodeWidth.IsWideCharacter(0xD7A3), Is.True);
        Assert.That(UnicodeWidth.IsWideCharacter(0xD7A4), Is.False);

        Assert.That(UnicodeWidth.IsWideCharacter(0xFF00), Is.False);
        Assert.That(UnicodeWidth.IsWideCharacter(0xFF01), Is.True);
        Assert.That(UnicodeWidth.IsWideCharacter(0xFF60), Is.True);
        Assert.That(UnicodeWidth.IsWideCharacter(0xFF61), Is.False);
    }

    /// <summary>Space and printable ASCII always occupy a single cell.</summary>
    [Test]
    public void SpaceAndPrintableAsciiReportWidthOne()
    {
        Assert.That(UnicodeWidth.GetWidth(0x20), Is.EqualTo(1));
        Assert.That(UnicodeWidth.GetWidth('0'), Is.EqualTo(1));
        Assert.That(UnicodeWidth.GetWidth('z'), Is.EqualTo(1));
    }

    /// <summary>EAW=Ambiguous code points fall back to single-width per policy.</summary>
    [Test]
    public void AmbiguousEawIsTreatedAsNarrow()
    {
        Assert.That(UnicodeWidth.GetWidth(0x00A1), Is.EqualTo(1));
        Assert.That(UnicodeWidth.GetWidth(0x00A4), Is.EqualTo(1));
        Assert.That(UnicodeWidth.GetWidth(0x2020), Is.EqualTo(1));
    }
}
