// <copyright file="TerminalBufferBackgroundDetectionTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Regression tests for the screen background-colour detector. These cover
/// the alt-buffer "btop sticks its bg" scenario: an app like btop fills the
/// alternate screen with its own background, and on exit the terminal must
/// revert to the user's default background instead of carrying btop's colour
/// forward.
/// </summary>
public class TerminalBufferBackgroundDetectionTests
{
    private const int UserDefaultBg = 0x1E1E1E;
    private const int UserDefaultFg = 0xCCCCCC;
    private const int BtopBg = 0x1F2533;

    /// <summary>
    /// While the alt buffer is filled with a non-default bg, the detector
    /// reports that bg; switching back to the main buffer must revert to the
    /// user's default bg even after the shell repaints with default-bg
    /// erase-to-EOL operations.
    /// </summary>
    [Test]
    public void AltBufferFill_RevertsToDefaultBg_AfterSwitchBack()
    {
        var buffer = new TerminalBuffer(20, 6);
        buffer.RecolorDefaults(UserDefaultFg, UserDefaultBg);

        // Force a frame so the detector observes the user's default bg.
        var initial = buffer.GetScreen();
        Assert.That(initial!.BackgroundColor, Is.EqualTo(UserDefaultBg));

        // Enter alt buffer (DECSET 1049) and fill every cell with btop's bg.
        buffer.SwitchToAlternateBuffer();
        buffer.SetBackgroundColor(BtopBg);
        for (int row = 0; row < 6; row++)
        {
            buffer.SetCursorPosition(row, 0);
            for (int col = 0; col < 20; col++)
            {
                buffer.PutChar(' ');
            }
        }

        var alt = buffer.GetScreen();
        Assert.That(alt!.BackgroundColor, Is.EqualTo(BtopBg), "Alt buffer should report btop's bg as dominant.");

        // Leave alt buffer (DECRST 1049). btop typically resets SGR before
        // exiting; emulate that.
        buffer.SetDefaultBackground();
        buffer.SwitchToMainBuffer();

        // The shell now repaints its prompt area with default-bg erase-to-EOL
        // operations. These must produce defaultBg cells, not the previous
        // detectedBg (which was btop's color).
        buffer.SetCursorPosition(0, 0);
        buffer.EraseInLine(0); // clear from cursor to EOL with current bg
        buffer.SetCursorPosition(1, 0);
        buffer.EraseInDisplay(0); // clear below cursor

        var restored = buffer.GetScreen();
        Assert.That(restored!.BackgroundColor, Is.EqualTo(UserDefaultBg), "Main buffer should revert to the user's default bg after btop exits.");
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 20; col++)
            {
                Assert.That(restored.Cells[row, col].ResolveBackground(restored.Palette), Is.EqualTo(UserDefaultBg), $"Cell [{row},{col}] should resolve to the user's default bg, not btop's bg.");
            }
        }
    }

    /// <summary>
    /// When no single bg colour holds a strict majority of the grid, the
    /// detector must fall back to <c>defaultBg</c> rather than sticking to
    /// the previously detected colour.
    /// </summary>
    [Test]
    public void DetectedBg_FallsBackToDefault_WhenNoMajority()
    {
        var buffer = new TerminalBuffer(4, 4);
        buffer.RecolorDefaults(UserDefaultFg, UserDefaultBg);

        // Seed: fill the screen with btop's bg so detectedBg flips to it.
        buffer.SetBackgroundColor(BtopBg);
        for (int row = 0; row < 4; row++)
        {
            buffer.SetCursorPosition(row, 0);
            for (int col = 0; col < 4; col++)
            {
                buffer.PutChar(' ');
            }
        }

        Assert.That(buffer.GetScreen()!.BackgroundColor, Is.EqualTo(BtopBg));

        // Now scribble a roughly even mix of three different bgs so no single
        // colour can claim > 50% of the 16 cells.
        int[] mix = { 0x111111, 0x222222, 0x333333, 0x444444 };
        for (int row = 0; row < 4; row++)
        {
            buffer.SetCursorPosition(row, 0);
            buffer.SetBackgroundColor(mix[row]);
            for (int col = 0; col < 4; col++)
            {
                buffer.PutChar(' ');
            }
        }

        var screen = buffer.GetScreen();
        Assert.That(screen!.BackgroundColor, Is.EqualTo(UserDefaultBg), "With no majority, detector must fall back to defaultBg, not the prior detected value.");
    }

    /// <summary>
    /// SGR 49 (default background) followed by erase-to-EOL on a clean main
    /// buffer must produce cells tagged with <c>defaultBg</c>, even after a
    /// previous full-screen TUI app temporarily skewed the detector.
    /// </summary>
    [Test]
    public void Sgr49AndEraseLine_ProducesDefaultBgCells_AfterAltBufferUse()
    {
        var buffer = new TerminalBuffer(10, 3);
        buffer.RecolorDefaults(UserDefaultFg, UserDefaultBg);
        _ = buffer.GetScreen();

        // Skew the detector via the alt buffer.
        buffer.SwitchToAlternateBuffer();
        buffer.SetBackgroundColor(BtopBg);
        for (int row = 0; row < 3; row++)
        {
            buffer.SetCursorPosition(row, 0);
            for (int col = 0; col < 10; col++)
            {
                buffer.PutChar(' ');
            }
        }

        _ = buffer.GetScreen();
        buffer.SwitchToMainBuffer();

        // Default background SGR + erase line.
        buffer.SetDefaultBackground();
        buffer.SetCursorPosition(0, 0);
        buffer.EraseInLine(2); // entire line

        var screen = buffer.GetScreen();
        for (int col = 0; col < 10; col++)
        {
            Assert.That(screen!.Cells[0, col].ResolveBackground(screen.Palette), Is.EqualTo(UserDefaultBg), $"Cell [0,{col}] must resolve to the user's default bg after SGR 49 + erase-line.");
        }
    }
}
