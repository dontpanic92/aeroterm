// <copyright file="GraphemeClusterTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests that the VT parser + terminal buffer treat grapheme clusters (ZWJ
/// emoji sequences, regional-indicator flags, skin-tone modifiers, keycaps,
/// VS16-presented emoji, and combining accents) as atomic cells with the
/// correct display width.
/// </summary>
[TestFixture]
public class GraphemeClusterTests
{
    /// <summary>A ZWJ family emoji collapses into one wide cell with the full cluster preserved.</summary>
    [Test]
    public void ZwjFamily_IsSingleWideCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "👨\u200D👩\u200D👧\u200D👦");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("👨\u200D👩\u200D👧\u200D👦"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null, "continuation cell");
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo(" "), "no third cell consumed");
    }

    /// <summary>A flag (regional-indicator pair) is a single wide cluster.</summary>
    [Test]
    public void RegionalIndicatorFlag_IsSingleWideCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "🇺🇸");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("🇺🇸"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo(" "));
    }

    /// <summary>Two flags land in two independent wide clusters (RI pairing resets).</summary>
    [Test]
    public void TwoFlags_AreTwoIndependentWideClusters()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "🇺🇸🇯🇵");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("🇺🇸"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo("🇯🇵"));
        Assert.That(screen.Cells[0, 3].Character, Is.Null);
    }

    /// <summary>Skin-tone modifier merges with the preceding emoji base into one wide cluster.</summary>
    [Test]
    public void SkinToneModifier_IsSingleWideCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "👋🏽");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("👋🏽"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
    }

    /// <summary>The keycap sequence "1" + VS16 + U+20E3 is one wide cluster.</summary>
    [Test]
    public void KeycapDigit_IsSingleWideCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "1\uFE0F\u20E3");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("1\uFE0F\u20E3"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo(" "));
    }

    /// <summary>Heart with VS16 is a wide emoji-presentation cluster.</summary>
    [Test]
    public void HeartWithVs16_IsWide()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "\u2764\uFE0F");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("\u2764\uFE0F"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
    }

    /// <summary>Heart with VS15 (text presentation) keeps the cluster but stays single-width.</summary>
    [Test]
    public void HeartWithVs15_IsNarrow()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "\u2764\uFE0E");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("\u2764\uFE0E"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo(" "), "no continuation cell");
    }

    /// <summary>A combining accent over 'e' stays in the 'e' cell with width 1.</summary>
    [Test]
    public void CombiningAccentOverE_IsNarrowCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "e\u0301");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("e\u0301"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo(" "));
    }

    /// <summary>A plain ASCII stream still writes one code point per cell.</summary>
    [Test]
    public void AsciiFastPath_StillWritesOneCharPerCell()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "abc");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("a"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo("b"));
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo("c"));
    }

    /// <summary>A control byte (LF) flushes a pending cluster before acting on the cursor.</summary>
    [Test]
    public void ControlByteFlushesPendingCluster()
    {
        var parser = MakeParser(out var buffer);
        Feed(parser, "\u2764\uFE0F\r\nX");

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("\u2764\uFE0F"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);

        // The 'X' lands on the next row thanks to CR+LF.
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("X"));
    }

    private static VtParser MakeParser(out TerminalBuffer buffer)
    {
        buffer = new TerminalBuffer(10, 10);
        return new VtParser(buffer, _ => { });
    }

    private static void Feed(VtParser parser, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        parser.Process(bytes);
    }
}
