// <copyright file="VtParserBenchmarks.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Benchmarks;

using System;
using System.Globalization;
using System.Text;
using AeroTerm.Pty;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Micro-benchmarks for <see cref="VtParser"/> throughput. Each benchmark
/// feeds a ~64 KiB pre-built byte buffer of representative ANSI output
/// (plain text, SGR, cursor addressing, OSC 8 hyperlinks, OSC 133 prompt
/// marks, and multi-codepoint grapheme clusters) through a fresh
/// <see cref="VtParser"/> + <see cref="TerminalBuffer"/> pair.
/// </summary>
[MemoryDiagnoser]
public class VtParserBenchmarks
{
    private const int TargetBytes = 64 * 1024;

    private byte[] plainText = Array.Empty<byte>();
    private byte[] sgrText = Array.Empty<byte>();
    private byte[] cursorText = Array.Empty<byte>();
    private byte[] hyperlinkText = Array.Empty<byte>();
    private byte[] shellIntegrationText = Array.Empty<byte>();
    private byte[] graphemeText = Array.Empty<byte>();
    private byte[] mixedText = Array.Empty<byte>();

    /// <summary>
    /// Builds the synthetic input buffers once. Each buffer is padded up to
    /// approximately <see cref="TargetBytes"/> bytes so all benchmarks
    /// process the same volume of data and can be compared directly.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.plainText = Repeat(BuildPlain(), TargetBytes);
        this.sgrText = Repeat(BuildSgr(), TargetBytes);
        this.cursorText = Repeat(BuildCursor(), TargetBytes);
        this.hyperlinkText = Repeat(BuildHyperlinks(), TargetBytes);
        this.shellIntegrationText = Repeat(BuildShellIntegration(), TargetBytes);
        this.graphemeText = Repeat(BuildGraphemes(), TargetBytes);
        this.mixedText = Repeat(BuildMixed(), TargetBytes);
    }

    /// <summary>
    /// Baseline: plain ASCII text with minimal control characters.
    /// </summary>
    /// <returns>The parsed byte count so the JIT cannot elide the call.</returns>
    [Benchmark(Baseline = true)]
    public int PlainText() => this.Parse(this.plainText);

    /// <summary>
    /// Stress: frequent SGR changes (color-rich log output).
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int SgrHeavy() => this.Parse(this.sgrText);

    /// <summary>
    /// Stress: frequent cursor addressing (TUI-style redraws).
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int CursorHeavy() => this.Parse(this.cursorText);

    /// <summary>
    /// Stress: OSC 8 hyperlink open/close sequences wrapping short labels.
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int Osc8Hyperlinks() => this.Parse(this.hyperlinkText);

    /// <summary>
    /// Stress: OSC 133 shell-integration prompt marks (A/B/C/D).
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int Osc133ShellIntegration() => this.Parse(this.shellIntegrationText);

    /// <summary>
    /// Stress: multi-codepoint grapheme clusters (emoji + ZWJ, combining
    /// marks, regional indicators). Exercises the cluster-aware path.
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int Graphemes() => this.Parse(this.graphemeText);

    /// <summary>
    /// Representative mix of all the above — closest to real-world shell
    /// output and a good single number for regression tracking.
    /// </summary>
    /// <returns>The parsed byte count.</returns>
    [Benchmark]
    public int MixedWorkload() => this.Parse(this.mixedText);

    private static byte[] Repeat(string fragment, int targetBytes)
    {
        byte[] unit = Encoding.UTF8.GetBytes(fragment);
        if (unit.Length == 0)
        {
            return unit;
        }

        int reps = Math.Max(1, targetBytes / unit.Length);
        byte[] buffer = new byte[unit.Length * reps];
        for (int i = 0; i < reps; i++)
        {
            Buffer.BlockCopy(unit, 0, buffer, i * unit.Length, unit.Length);
        }

        return buffer;
    }

    private static string BuildPlain()
    {
        var sb = new StringBuilder(2048);
        for (int r = 0; r < 24; r++)
        {
            sb.Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ");
            sb.Append("Pellentesque habitant morbi tristique senectus et netus.");
            sb.Append('\r').Append('\n');
        }

        return sb.ToString();
    }

    private static string BuildSgr()
    {
        var sb = new StringBuilder(2048);
        for (int r = 0; r < 16; r++)
        {
            sb.Append("\x1b[1;31mred \x1b[32mgreen \x1b[34mblue \x1b[0mplain ");
            sb.Append("\x1b[38;2;200;150;50mtruecolor\x1b[0m ");
            sb.Append("\x1b[4munderline\x1b[24m \x1b[7mreverse\x1b[27m");
            sb.Append('\r').Append('\n');
        }

        return sb.ToString();
    }

    private static string BuildCursor()
    {
        var sb = new StringBuilder(2048);
        sb.Append("\x1b[H\x1b[2J");
        for (int r = 0; r < 48; r++)
        {
            sb.Append(string.Format(CultureInfo.InvariantCulture, "\x1b[{0};1Hrow {1}", (r % 24) + 1, r));
            sb.Append("\x1b[K");
        }

        return sb.ToString();
    }

    private static string BuildHyperlinks()
    {
        var sb = new StringBuilder(2048);
        for (int i = 0; i < 16; i++)
        {
            sb.Append("\x1b]8;;https://example.com/path/");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append("\x1b\\");
            sb.Append("click here");
            sb.Append("\x1b]8;;\x1b\\");
            sb.Append(' ');
            if ((i & 3) == 3)
            {
                sb.Append('\r').Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string BuildShellIntegration()
    {
        var sb = new StringBuilder(2048);
        for (int i = 0; i < 8; i++)
        {
            sb.Append("\x1b]133;A\x1b\\");
            sb.Append("user@host:~/work$ ");
            sb.Append("\x1b]133;B\x1b\\");
            sb.Append("ls -la");
            sb.Append('\r').Append('\n');
            sb.Append("\x1b]133;C\x1b\\");
            sb.Append("total 42\r\ndrwxr-xr-x  3 user user 4096 Jan  1 00:00 .\r\n");
            sb.Append("\x1b]133;D;0\x1b\\");
        }

        return sb.ToString();
    }

    private static string BuildGraphemes()
    {
        var sb = new StringBuilder(2048);
        for (int r = 0; r < 16; r++)
        {
            // Family ZWJ sequence.
            sb.Append("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 ");

            // Flag (regional indicators).
            sb.Append("\U0001F1EF\U0001F1F5 ");

            // Emoji + skin-tone modifier.
            sb.Append("\U0001F44B\U0001F3FD ");

            // Latin letter + combining acute.
            sb.Append("a\u0301e\u0301i\u0301 ");

            // Devanagari cluster.
            sb.Append("\u0928\u092E\u0938\u094D\u0924\u0947 ");

            sb.Append('\r').Append('\n');
        }

        return sb.ToString();
    }

    private static string BuildMixed()
    {
        var sb = new StringBuilder(8192);
        sb.Append(BuildShellIntegration());
        sb.Append(BuildPlain());
        sb.Append(BuildSgr());
        sb.Append(BuildHyperlinks());
        sb.Append(BuildGraphemes());
        sb.Append(BuildCursor());
        return sb.ToString();
    }

    private int Parse(byte[] data)
    {
        var buffer = new TerminalBuffer(120, 40);
        var parser = new VtParser(buffer, _ => { });
        parser.Process(data);
        return data.Length;
    }
}
