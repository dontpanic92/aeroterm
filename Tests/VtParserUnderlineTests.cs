// <copyright file="VtParserUnderlineTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests covering the full SGR underline/undercurl/strikethrough matrix,
/// including ITU T.416 colon sub-parameter forms for SGR 4:n and SGR 58.
/// </summary>
public class VtParserUnderlineTests
{
    /// <summary>SGR 21 should enable double underline.</summary>
    [Test]
    public void Sgr21_EnablesDoubleUnderline()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[21mA"));
        var cell = buffer.GetScreen()!.Cells[0, 0];
        Assert.That(cell.DoubleUnderline, Is.True);
        Assert.That(cell.Underline, Is.False);
        Assert.That(cell.UnderlineStyle, Is.EqualTo(UnderlineStyle.Double));
    }

    /// <summary>SGR 4:2 should enable double underline (alt encoding).</summary>
    [Test]
    public void Sgr4Sub2_EnablesDoubleUnderline()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[4:2mA"));
        var cell = buffer.GetScreen()!.Cells[0, 0];
        Assert.That(cell.DoubleUnderline, Is.True);
        Assert.That(cell.UnderlineStyle, Is.EqualTo(UnderlineStyle.Double));
    }

    /// <summary>SGR 4:3 should enable undercurl.</summary>
    [Test]
    public void Sgr4Sub3_EnablesUndercurl()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[4:3mA"));
        var cell = buffer.GetScreen()!.Cells[0, 0];
        Assert.That(cell.Undercurl, Is.True);
        Assert.That(cell.UnderlineStyle, Is.EqualTo(UnderlineStyle.Curly));
    }

    /// <summary>SGR 4:0 should clear all underline variants.</summary>
    [Test]
    public void Sgr4Sub0_ClearsAllUnderlineVariants()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[4:3m\x1b[4:0mA"));
        var cell = buffer.GetScreen()!.Cells[0, 0];
        Assert.That(cell.Undercurl, Is.False);
        Assert.That(cell.Underline, Is.False);
        Assert.That(cell.DoubleUnderline, Is.False);
        Assert.That(cell.UnderlineStyle, Is.EqualTo(UnderlineStyle.None));
    }

    /// <summary>SGR 9 enables strikethrough, SGR 29 clears it.</summary>
    [Test]
    public void Sgr9Then29_TogglesStrikethrough()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[9mA\x1b[29mB"));
        var cells = buffer.GetScreen()!.Cells;
        Assert.That(cells[0, 0].Strikethrough, Is.True);
        Assert.That(cells[0, 1].Strikethrough, Is.False);
    }

    /// <summary>SGR 58;2;R;G;B sets truecolor underline color via semicolons.</summary>
    [Test]
    public void Sgr58SemicolonTruecolor_SetsSpecialColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58;2;10;20;30mA"));
        Assert.That(buffer.GetScreen()!.Cells[0, 0].SpecialColor, Is.EqualTo((10 << 16) | (20 << 8) | 30));
    }

    /// <summary>SGR 58:2::R:G:B sets truecolor underline color via ITU T.416 colon form with empty color-space slot.</summary>
    [Test]
    public void Sgr58ColonTruecolorWithEmptyColorSpace_SetsSpecialColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58:2::40:50:60mA"));
        Assert.That(buffer.GetScreen()!.Cells[0, 0].SpecialColor, Is.EqualTo((40 << 16) | (50 << 8) | 60));
    }

    /// <summary>SGR 58:2:R:G:B (compact colon form without color-space slot) also works.</summary>
    [Test]
    public void Sgr58ColonTruecolorCompact_SetsSpecialColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58:2:70:80:90mA"));
        Assert.That(buffer.GetScreen()!.Cells[0, 0].SpecialColor, Is.EqualTo((70 << 16) | (80 << 8) | 90));
    }

    /// <summary>SGR 58;5;N sets the 256-color underline color via semicolons.</summary>
    [Test]
    public void Sgr58SemicolonIndexed_SetsSpecialColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58;5;196mA"));
        int color = buffer.GetScreen()!.Cells[0, 0].SpecialColor;
        Assert.That(color, Is.Not.EqualTo(0));
    }

    /// <summary>SGR 58:5:N colon form for 256-color underline color.</summary>
    [Test]
    public void Sgr58ColonIndexed_SetsSpecialColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58:5:196mA"));
        int color = buffer.GetScreen()!.Cells[0, 0].SpecialColor;
        Assert.That(color, Is.Not.EqualTo(0));
    }

    /// <summary>SGR 59 resets the underline color to default (zero sentinel).</summary>
    [Test]
    public void Sgr59_ResetsUnderlineColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[58;2;10;20;30m\x1b[59mA"));
        Assert.That(buffer.GetScreen()!.Cells[0, 0].SpecialColor, Is.EqualTo(0));
    }

    /// <summary>SGR 38:2::R:G:B colon form should also work for foreground truecolor.</summary>
    [Test]
    public void Sgr38ColonTruecolor_SetsForegroundColor()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[38:2::11:22:33mA"));
        Assert.That(buffer.GetScreen()!.Cells[0, 0].ForegroundColor, Is.EqualTo((11 << 16) | (22 << 8) | 33));
    }

    /// <summary>Combined SGR with both colon sub-params and further semicolon-separated codes.</summary>
    [Test]
    public void Sgr4Sub3ThenBold_BothApplied()
    {
        var (buffer, parser) = Setup();
        parser.Process(Encoding.ASCII.GetBytes("\x1b[4:3;1mA"));
        var cell = buffer.GetScreen()!.Cells[0, 0];
        Assert.That(cell.Undercurl, Is.True);
        Assert.That(cell.Bold, Is.True);
    }

    private static (TerminalBuffer Buffer, VtParser Parser) Setup()
    {
        var buffer = new TerminalBuffer(4, 1);
        var parser = new VtParser(buffer, _ => { });
        return (buffer, parser);
    }
}
