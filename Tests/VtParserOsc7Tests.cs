// <copyright file="VtParserOsc7Tests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for OSC 7 current-working-directory reporting.
/// </summary>
public class VtParserOsc7Tests
{
    /// <summary>OSC 7 with a POSIX file URI raises the cwd event.</summary>
    [Test]
    public void Osc7_PosixFileUri_RaisesCurrentDirectory()
    {
        string? captured = Capture("\x1B]7;file://localhost/home/me\x1B\\");
        Assert.That(captured, Is.EqualTo("/home/me"));
    }

    /// <summary>OSC 7 percent-decoded paths are surfaced as filesystem paths.</summary>
    [Test]
    public void Osc7_EscapedFileUri_DecodesPath()
    {
        string? captured = Capture("\x1B]7;file://host/Users/me/Hello%20World\x1B\\");
        Assert.That(captured, Is.EqualTo("/Users/me/Hello World"));
    }

    /// <summary>OSC 7 Windows drive file URIs are normalized to Windows paths.</summary>
    [Test]
    public void Osc7_WindowsDriveFileUri_NormalizesDrivePath()
    {
        string? captured = Capture("\x1B]7;file://host/C:/Users/me\x1B\\");
        Assert.That(captured, Is.EqualTo(@"C:\Users\me"));
    }

    /// <summary>Non-file or malformed OSC 7 payloads are ignored.</summary>
    [Test]
    public void Osc7_InvalidPayload_IsIgnored()
    {
        var parser = new VtParser(new TerminalBuffer(2, 1), _ => { });
        int count = 0;
        parser.CurrentDirectoryChanged += (_, _) => count++;

        parser.Process(Encoding.UTF8.GetBytes("\x1B]7;http://example.test/home/me\x1B\\"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B]7;not a uri\x1B\\"));

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
