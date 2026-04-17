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
/// Micro-benchmarks for <see cref="VtParser"/> throughput against synthetic
/// workloads (plain text, SGR-heavy colored output, cursor-motion-heavy output).
/// </summary>
[MemoryDiagnoser]
public class VtParserBenchmarks
{
    private byte[] plainText = Array.Empty<byte>();
    private byte[] sgrText = Array.Empty<byte>();
    private byte[] cursorText = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the number of rendered "screens" (80x24) per iteration.
    /// </summary>
    [Params(10, 100)]
    public int Screens { get; set; }

    /// <summary>
    /// Builds the synthetic input buffers once per parameter combination.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var plain = new StringBuilder();
        var sgr = new StringBuilder();
        var cursor = new StringBuilder();

        for (int s = 0; s < this.Screens; s++)
        {
            for (int r = 0; r < 24; r++)
            {
                plain.Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ");
                plain.Append('\r').Append('\n');

                sgr.Append("\x1b[1;31mred \x1b[32mgreen \x1b[34mblue \x1b[0mplain ");
                sgr.Append("\x1b[38;2;200;150;50mtruecolor\x1b[0m ");
                sgr.Append('\r').Append('\n');

                cursor.Append("\x1b[H\x1b[2J");
                cursor.Append(string.Format(CultureInfo.InvariantCulture, "\x1b[{0};1Hrow {1}", (r % 24) + 1, r));
            }
        }

        this.plainText = Encoding.UTF8.GetBytes(plain.ToString());
        this.sgrText = Encoding.UTF8.GetBytes(sgr.ToString());
        this.cursorText = Encoding.UTF8.GetBytes(cursor.ToString());
    }

    /// <summary>
    /// Baseline: feed plain ASCII text with minimal control chars.
    /// </summary>
    /// <returns>The parser-scoped total byte count so the JIT can't elide the call.</returns>
    [Benchmark(Baseline = true)]
    public int PlainText()
    {
        var buffer = new TerminalBuffer(80, 24);
        var parser = new VtParser(buffer, _ => { });
        parser.Process(this.plainText);
        return this.plainText.Length;
    }

    /// <summary>
    /// Stress: frequent SGR changes (color-rich log output).
    /// </summary>
    /// <returns>Input byte count.</returns>
    [Benchmark]
    public int SgrHeavy()
    {
        var buffer = new TerminalBuffer(80, 24);
        var parser = new VtParser(buffer, _ => { });
        parser.Process(this.sgrText);
        return this.sgrText.Length;
    }

    /// <summary>
    /// Stress: frequent cursor addressing (TUI-style redraws).
    /// </summary>
    /// <returns>Input byte count.</returns>
    [Benchmark]
    public int CursorHeavy()
    {
        var buffer = new TerminalBuffer(80, 24);
        var parser = new VtParser(buffer, _ => { });
        parser.Process(this.cursorText);
        return this.cursorText.Length;
    }
}
