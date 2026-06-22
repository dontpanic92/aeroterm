// <copyright file="VtParserOsc9Tests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for OSC 9;9 (ConEmu) current-working-directory reporting.
/// </summary>
public class VtParserOsc9Tests
{
    /// <summary>OSC 9;9 with a Windows path raises the cwd event verbatim.</summary>
    [Test]
    public void Osc99_WindowsPath_RaisesCurrentDirectory()
    {
        string? captured = Capture("\x1B]9;9;C:\\Users\\me\\repo\x07");
        Assert.That(captured, Is.EqualTo(@"C:\Users\me\repo"));
    }

    /// <summary>OSC 9;9 with a quoted path strips the surrounding quotes.</summary>
    [Test]
    public void Osc99_QuotedPath_StripsQuotes()
    {
        string? captured = Capture("\x1B]9;9;\"C:\\Program Files\\app\"\x07");
        Assert.That(captured, Is.EqualTo(@"C:\Program Files\app"));
    }

    /// <summary>OSC 9;9 terminated with ST is recognised.</summary>
    [Test]
    public void Osc99_StTerminated_RaisesCurrentDirectory()
    {
        string? captured = Capture("\x1B]9;9;/home/me\x1B\\");
        Assert.That(captured, Is.EqualTo("/home/me"));
    }

    /// <summary>Other OSC 9 sub-commands (e.g. progress) are ignored.</summary>
    [Test]
    public void Osc9_NonCwdSubcommand_IsIgnored()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        int count = 0;
        parser.CurrentDirectoryChanged += (_, _) => count++;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]9;4;1;50\x07"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B]9;Toast notification\x07"));

        Assert.That(count, Is.EqualTo(0));
    }

    private static string? Capture(string sequence)
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        string? captured = null;
        parser.CurrentDirectoryChanged += (_, e) => captured = e.CurrentDirectory;
        parser.Process(Encoding.UTF8.GetBytes(sequence));
        return captured;
    }
}
