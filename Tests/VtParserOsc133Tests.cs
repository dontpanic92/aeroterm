// <copyright file="VtParserOsc133Tests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Collections.Generic;
using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for the OSC 133 / OSC 633 prompt-mark parser path: verifies the
/// <see cref="VtParser.PromptMarkRaised"/> engine event, exit-code and
/// cwd extraction, and graceful handling of unknown subcommands.
/// </summary>
public class VtParserOsc133Tests
{
    /// <summary>OSC 133;A produces a PromptStart mark.</summary>
    [Test]
    public void Osc133_A_RaisesPromptStart()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;A\x1B\\");
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.PromptStart));
        Assert.That(captured.ExitCode, Is.Null);
        Assert.That(captured.CurrentDirectory, Is.Null);
    }

    /// <summary>OSC 133;B produces a CommandStart mark.</summary>
    [Test]
    public void Osc133_B_RaisesCommandStart()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;B\x1B\\");
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.CommandStart));
    }

    /// <summary>OSC 133;C produces an OutputStart mark.</summary>
    [Test]
    public void Osc133_C_RaisesOutputStart()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;C\x1B\\");
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.OutputStart));
    }

    /// <summary>OSC 133;D produces a CommandEnd mark with no exit code.</summary>
    [Test]
    public void Osc133_D_NoPayload_RaisesCommandEnd()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;D\x1B\\");
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.CommandEnd));
        Assert.That(captured.ExitCode, Is.Null);
    }

    /// <summary>OSC 133;D;42 parses exit code 42.</summary>
    [Test]
    public void Osc133_D_WithExitCode_ParsesExitCode()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;D;42\x1B\\");
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.CommandEnd));
        Assert.That(captured.ExitCode, Is.EqualTo(42));
    }

    /// <summary>OSC 133;A;cwd=/home/me captures the working directory.</summary>
    [Test]
    public void Osc133_A_WithCwd_CapturesCwd()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]133;A;cwd=/home/me\x1B\\");
        Assert.That(captured!.Kind, Is.EqualTo(PromptMarkKind.PromptStart));
        Assert.That(captured.CurrentDirectory, Is.EqualTo("/home/me"));
    }

    /// <summary>Unknown sub-letters are silently ignored, not crashed on.</summary>
    [Test]
    public void Osc133_UnknownKind_IsIgnored()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        int count = 0;
        parser.PromptMarkRaised += (_, _) => count++;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;Z\x1B\\"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;P;prop=x\x1B\\"));

        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>The VS Code-compatible OSC 633 variant maps to the same kinds.</summary>
    [Test]
    public void Osc633_AllKinds_MapSameAsOsc133()
    {
        var kinds = new List<PromptMarkKind>();
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        parser.PromptMarkRaised += (_, e) => kinds.Add(e.Kind);

        parser.Process(Encoding.UTF8.GetBytes(
            "\x1B]633;A\x1B\\\x1B]633;B\x1B\\\x1B]633;C\x1B\\\x1B]633;D;7\x1B\\"));

        Assert.That(
            kinds,
            Is.EqualTo(new[]
            {
                PromptMarkKind.PromptStart,
                PromptMarkKind.CommandStart,
                PromptMarkKind.OutputStart,
                PromptMarkKind.CommandEnd,
            }));
    }

    /// <summary>All four 133 kinds flow through in order.</summary>
    [Test]
    public void Osc133_AllKinds_OrderedEmission()
    {
        var kinds = new List<PromptMarkKind>();
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        parser.PromptMarkRaised += (_, e) => kinds.Add(e.Kind);

        parser.Process(Encoding.UTF8.GetBytes(
            "\x1B]133;A\x1B\\\x1B]133;B\x1B\\\x1B]133;C\x1B\\\x1B]133;D\x1B\\"));

        Assert.That(
            kinds,
            Is.EqualTo(new[]
            {
                PromptMarkKind.PromptStart,
                PromptMarkKind.CommandStart,
                PromptMarkKind.OutputStart,
                PromptMarkKind.CommandEnd,
            }));
    }

    /// <summary>Empty OSC 133 payload is a silent no-op.</summary>
    [Test]
    public void Osc133_EmptyPayload_IsNoop()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        int count = 0;
        parser.PromptMarkRaised += (_, _) => count++;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]133;\x1B\\"));

        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>D;err=3 exit code form (alt VS Code-style key).</summary>
    [Test]
    public void Osc133_D_WithErrKey_ParsesExitCode()
    {
        PromptMarkEventArgs? captured = Capture("\x1B]633;D;err=3\x1B\\");
        Assert.That(captured!.ExitCode, Is.EqualTo(3));
    }

    private static PromptMarkEventArgs? Capture(string sequence)
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        PromptMarkEventArgs? captured = null;
        parser.PromptMarkRaised += (_, e) => captured = e;
        parser.Process(Encoding.UTF8.GetBytes(sequence));
        return captured;
    }
}
