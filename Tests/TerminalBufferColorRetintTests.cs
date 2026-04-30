// <copyright file="TerminalBufferColorRetintTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Regression tests for the logical-color refactor: cells store palette
/// references / default sentinels rather than baked RGB, so scheme and
/// palette changes must retroactively retint both live and scrollback
/// content.
/// </summary>
public class TerminalBufferColorRetintTests
{
    private const int SchemeAFg = 0xAAAAAA;
    private const int SchemeABg = 0x111111;
    private const int SchemeBFg = 0x222222;
    private const int SchemeBBg = 0xEEEEEE;

    /// <summary>
    /// Cells written with the default fg/bg should resolve to scheme B
    /// after <see cref="TerminalBuffer.RecolorDefaults"/>, including
    /// rows that were already pushed into the scrollback ring.
    /// </summary>
    [Test]
    public void RecolorDefaults_RetintsScrollbackDefaultCells()
    {
        var buffer = new TerminalBuffer(4, 2) { ScrollbackLimit = 100 };
        buffer.RecolorDefaults(SchemeAFg, SchemeABg);
        var parser = new VtParser(buffer, _ => { });

        // Fill 4 rows with default-color text, then scroll past them so
        // they live in the scrollback ring.
        parser.Process(Encoding.UTF8.GetBytes("aaaa\r\nbbbb\r\ncccc\r\ndddd\r\neeee\r\nffff\r\n"));

        Assert.That(buffer.ScrollbackCount, Is.GreaterThan(0), "Setup expected scrollback to accumulate.");

        buffer.RecolorDefaults(SchemeBFg, SchemeBBg);

        var screen = buffer.GetScreen();
        Assert.That(screen!.BackgroundColor, Is.EqualTo(SchemeBBg));

        for (int i = 0; i < buffer.ScrollbackCount; i++)
        {
            var row = buffer.GetScrollbackLine(i);
            for (int j = 0; j < row.Length; j++)
            {
                Assert.That(row[j].ResolveBackground(screen.Palette), Is.EqualTo(SchemeBBg), $"Scrollback cell [{i},{j}] bg should follow scheme B.");
                Assert.That(row[j].ResolveForeground(screen.Palette), Is.EqualTo(SchemeBFg), $"Scrollback cell [{i},{j}] fg should follow scheme B.");
            }
        }
    }

    /// <summary>
    /// Cells written via SGR 31 (palette index 1) must retint when the
    /// palette is overwritten — even cells already in scrollback.
    /// </summary>
    [Test]
    public void SetAnsiPalette_RetintsScrollbackPaletteCells()
    {
        var buffer = new TerminalBuffer(2, 2) { ScrollbackLimit = 100 };
        var parser = new VtParser(buffer, _ => { });

        // Scheme A's red.
        var paletteA = new int[16];
        for (int i = 0; i < 16; i++)
        {
            paletteA[i] = 0;
        }

        paletteA[1] = 0xCC0000;
        buffer.SetAnsiPalette(paletteA);

        parser.Process(Encoding.UTF8.GetBytes("\x1B[31mAA\r\n\x1B[31mBB\r\n\x1B[31mCC\r\n\x1B[31mDD\r\n"));
        Assert.That(buffer.ScrollbackCount, Is.GreaterThan(0));

        // Switch to scheme B's red.
        var paletteB = (int[])paletteA.Clone();
        paletteB[1] = 0x0000CC;
        buffer.SetAnsiPalette(paletteB);

        var screen = buffer.GetScreen();
        for (int i = 0; i < buffer.ScrollbackCount; i++)
        {
            var row = buffer.GetScrollbackLine(i);
            for (int j = 0; j < row.Length; j++)
            {
                if (row[j].Character == "A" || row[j].Character == "B" || row[j].Character == "C" || row[j].Character == "D")
                {
                    Assert.That(row[j].ResolveForeground(screen!.Palette), Is.EqualTo(0x0000CC), $"Scrollback palette cell [{i},{j}] should follow new palette.");
                }
            }
        }
    }

    /// <summary>
    /// Truecolor cells (SGR 38;2;R;G;B) are immune to palette and
    /// scheme changes by VT spec.
    /// </summary>
    [Test]
    public void Truecolor_SurvivesSchemeAndPaletteChanges()
    {
        var buffer = new TerminalBuffer(2, 1);
        var parser = new VtParser(buffer, _ => { });
        buffer.RecolorDefaults(SchemeAFg, SchemeABg);

        parser.Process(Encoding.UTF8.GetBytes("\x1B[38;2;100;150;200mZ"));

        var before = buffer.GetScreen();
        int beforeFg = before!.Cells[0, 0].ResolveForeground(before.Palette);

        var newPalette = new int[16];
        for (int i = 0; i < 16; i++)
        {
            newPalette[i] = 0xABCDEF;
        }

        buffer.SetAnsiPalette(newPalette);
        buffer.RecolorDefaults(SchemeBFg, SchemeBBg);

        var after = buffer.GetScreen();
        Assert.That(after!.Cells[0, 0].ResolveForeground(after.Palette), Is.EqualTo(beforeFg));
        Assert.That(after.Cells[0, 0].ForegroundColor, Is.EqualTo((100 << 16) | (150 << 8) | 200));
    }

    /// <summary>
    /// OSC 4 palette overrides must retroactively retint cells using
    /// that palette index — including cells in the scrollback ring.
    /// </summary>
    [Test]
    public void SetPaletteColor_RetintsExistingCells()
    {
        var buffer = new TerminalBuffer(2, 2) { ScrollbackLimit = 100 };
        var parser = new VtParser(buffer, _ => { });

        parser.Process(Encoding.UTF8.GetBytes("\x1B[38;5;200mAB\r\nCD\r\nEF\r\nGH\r\n"));
        Assert.That(buffer.ScrollbackCount, Is.GreaterThan(0));

        // OSC-style override of palette[200].
        const int Override = 0x123456;
        buffer.SetPaletteColor(200, Override);

        var screen = buffer.GetScreen();
        bool sawHit = false;
        for (int i = 0; i < buffer.ScrollbackCount; i++)
        {
            var row = buffer.GetScrollbackLine(i);
            for (int j = 0; j < row.Length; j++)
            {
                if (row[j].Character is { Length: > 0 } && row[j].Character != " ")
                {
                    Assert.That(row[j].ResolveForeground(screen!.Palette), Is.EqualTo(Override));
                    sawHit = true;
                }
            }
        }

        Assert.That(sawHit, Is.True, "Test expected at least one scrollback cell to verify.");
    }

    /// <summary>
    /// DECSC / DECRC must preserve logical (not resolved) colors so a
    /// later scheme change still affects the restored attributes.
    /// </summary>
    [Test]
    public void SaveRestoreCursor_PreservesLogicalColors()
    {
        var buffer = new TerminalBuffer(4, 1);
        var parser = new VtParser(buffer, _ => { });
        buffer.RecolorDefaults(SchemeAFg, SchemeABg);

        // Set red fg, save, change to truecolor, restore, write — the
        // restored cell should still be the (logical) palette red.
        parser.Process(Encoding.UTF8.GetBytes("\x1B[31m\x1B7\x1B[38;2;1;2;3m\x1B8X"));

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].ForegroundColor, Is.EqualTo(ColorRef.Palette(1)));
    }
}
