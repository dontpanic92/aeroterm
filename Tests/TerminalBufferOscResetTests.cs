// <copyright file="TerminalBufferOscResetTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Regression tests for OSC 110/111 (reset terminal default fg/bg). These
/// guard the "Copilot CLI exit turns background pure white" scenario: a TUI
/// emits OSC 110/111 on exit to restore terminal defaults, and AeroTerm must
/// restore the *theme* baseline (set via <see cref="TerminalBuffer.RecolorDefaults"/>)
/// rather than xterm's hard-coded black/white.
/// </summary>
public class TerminalBufferOscResetTests
{
    private const int ThemeFg = 0xCCCCCC;
    private const int ThemeBg = 0x1E1E1E;

    /// <summary>
    /// Direct API: after the theme baseline is installed, OSC 111 (reset
    /// default background) must restore the theme bg, not 0xFFFFFF.
    /// </summary>
    [Test]
    public void ResetTerminalDefaultBackground_RestoresThemeBaseline()
    {
        var buffer = new TerminalBuffer(4, 2);
        buffer.RecolorDefaults(ThemeFg, ThemeBg);

        buffer.SetTerminalDefaultBackground(0x808080);
        Assert.That(buffer.DefaultBackground, Is.EqualTo(0x808080));

        buffer.ResetTerminalDefaultBackground();

        Assert.That(buffer.DefaultBackground, Is.EqualTo(ThemeBg));
    }

    /// <summary>
    /// Direct API: after the theme baseline is installed, OSC 110 (reset
    /// default foreground) must restore the theme fg, not 0x000000.
    /// </summary>
    [Test]
    public void ResetTerminalDefaultForeground_RestoresThemeBaseline()
    {
        var buffer = new TerminalBuffer(4, 2);
        buffer.RecolorDefaults(ThemeFg, ThemeBg);

        buffer.SetTerminalDefaultForeground(0x808080);
        Assert.That(buffer.DefaultForeground, Is.EqualTo(0x808080));

        buffer.ResetTerminalDefaultForeground();

        Assert.That(buffer.DefaultForeground, Is.EqualTo(ThemeFg));
    }

    /// <summary>
    /// Full VT path: a process emits an OSC 111 escape sequence — the
    /// parser must drive <see cref="TerminalBuffer.ResetTerminalDefaultBackground"/>
    /// and end up at the theme baseline.
    /// </summary>
    [Test]
    public void Osc111Sequence_RestoresThemeBaseline()
    {
        var buffer = new TerminalBuffer(4, 2);
        buffer.RecolorDefaults(ThemeFg, ThemeBg);
        var parser = new VtParser(buffer, _ => { });

        parser.Process(Encoding.UTF8.GetBytes("\x1B]11;#FFFFFF\x1B\\"));
        Assert.That(buffer.DefaultBackground, Is.EqualTo(0xFFFFFF));

        parser.Process(Encoding.UTF8.GetBytes("\x1B]111\x1B\\"));

        Assert.That(buffer.DefaultBackground, Is.EqualTo(ThemeBg));
    }

    /// <summary>
    /// Regression for the Copilot CLI exit scenario: enter alt buffer,
    /// override the bg with OSC 11, switch back to the main buffer, then
    /// emit OSC 111. The next rendered screen must report the theme bg, not
    /// white.
    /// </summary>
    [Test]
    public void AltBufferExit_WithOsc111_RevertsScreenToThemeBg()
    {
        var buffer = new TerminalBuffer(20, 6);
        buffer.RecolorDefaults(ThemeFg, ThemeBg);
        var parser = new VtParser(buffer, _ => { });

        var initial = buffer.GetScreen();
        Assert.That(initial!.BackgroundColor, Is.EqualTo(ThemeBg));

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?1049h"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B]11;#202020\x1B\\"));
        _ = buffer.GetScreen();

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?1049l"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B]111\x1B\\"));

        var screen = buffer.GetScreen();

        Assert.That(buffer.DefaultBackground, Is.EqualTo(ThemeBg));
        Assert.That(screen!.BackgroundColor, Is.EqualTo(ThemeBg));
    }
}
