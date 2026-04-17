// <copyright file="VtParserExtensionsTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for newly-added VT parser features: BEL event, OSC 8 hyperlinks,
/// and OSC 133 shell integration.
/// </summary>
public class VtParserExtensionsTests
{
    /// <summary>
    /// A BEL byte (0x07) in the ground state should raise the BellRaised event.
    /// </summary>
    [Test]
    public void Process_Bel_RaisesBellEvent()
    {
        var buffer = new TerminalBuffer(2, 1);
        var parser = new VtParser(buffer, _ => { });
        int bellCount = 0;
        parser.BellRaised += (_, _) => bellCount++;

        parser.Process(new byte[] { (byte)'A', 0x07, (byte)'B', 0x07 });

        Assert.That(bellCount, Is.EqualTo(2));
    }

    /// <summary>
    /// OSC 8 should stamp subsequent cells with the hyperlink URI and id.
    /// </summary>
    [Test]
    public void Process_Osc8Hyperlink_StampsCells()
    {
        var buffer = new TerminalBuffer(5, 1);
        var parser = new VtParser(buffer, _ => { });

        parser.Process(Encoding.UTF8.GetBytes(
            "\x1B]8;id=42;https://example.com\x1B\\link\x1B]8;;\x1B\\X"));

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.Cells[0, 0].HyperlinkUri, Is.EqualTo("https://example.com"));
        Assert.That(screen.Cells[0, 0].HyperlinkId, Is.EqualTo("42"));
        Assert.That(screen.Cells[0, 3].HyperlinkUri, Is.EqualTo("https://example.com"));
        Assert.That(screen.Cells[0, 4].HyperlinkUri, Is.Null);
        Assert.That(screen.Cells[0, 4].Character, Is.EqualTo("X"));
    }

    /// <summary>
    /// OSC 8 with empty URI should clear hyperlink state.
    /// </summary>
    [Test]
    public void Process_Osc8EmptyUri_ClearsHyperlink()
    {
        var buffer = new TerminalBuffer(3, 1);
        var parser = new VtParser(buffer, _ => { });

        parser.Process(Encoding.UTF8.GetBytes("\x1B]8;;https://a.test\x1B\\A\x1B]8;;\x1B\\B"));

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].HyperlinkUri, Is.EqualTo("https://a.test"));
        Assert.That(screen.Cells[0, 1].HyperlinkUri, Is.Null);
    }

    /// <summary>
    /// OSC 133;A should surface as a PromptStart shell-integration event.
    /// </summary>
    [Test]
    public void Process_Osc133PromptStart_RaisesShellIntegrationEvent()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        ShellIntegrationEventArgs? captured = null;
        parser.ShellIntegrationReceived += (_, e) => captured = e;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;A\x1B\\"));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Kind, Is.EqualTo(ShellIntegrationKind.PromptStart));
    }

    /// <summary>
    /// OSC 133;D;0 should surface as a CommandFinished event with exit code 0.
    /// </summary>
    [Test]
    public void Process_Osc133CommandFinishedWithExitCode_ParsesExitCode()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        ShellIntegrationEventArgs? captured = null;
        parser.ShellIntegrationReceived += (_, e) => captured = e;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;D;42\x1B\\"));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Kind, Is.EqualTo(ShellIntegrationKind.CommandFinished));
        Assert.That(captured.ExitCode, Is.EqualTo(42));
    }

    /// <summary>
    /// OSC 133 with an unknown kind letter should be silently ignored.
    /// </summary>
    [Test]
    public void Process_Osc133UnknownKind_Ignored()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        int count = 0;
        parser.ShellIntegrationReceived += (_, _) => count++;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;Z\x1B\\"));

        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>
    /// All four OSC 133 prompt-mark kinds should surface correctly.
    /// </summary>
    [Test]
    public void Process_Osc133AllKinds_SurfacesCorrectly()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        var kinds = new List<ShellIntegrationKind>();
        parser.ShellIntegrationReceived += (_, e) => kinds.Add(e.Kind);

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;A\x1B\\\x1B]133;B\x1B\\\x1B]133;C\x1B\\\x1B]133;D\x1B\\"));

        Assert.That(
            kinds,
            Is.EqualTo(new[]
            {
                ShellIntegrationKind.PromptStart,
                ShellIntegrationKind.CommandStart,
                ShellIntegrationKind.CommandExecuted,
                ShellIntegrationKind.CommandFinished,
            }));
    }
}
