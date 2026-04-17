// <copyright file="TerminalBufferBenchmarks.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Benchmarks;

using System;
using AeroTerm.Pty;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Micro-benchmarks for the <see cref="TerminalBuffer"/> hot paths that the
/// renderer feeds into: <see cref="TerminalBuffer.PutCluster(string, int)"/>
/// for sustained writes, and <see cref="TerminalBuffer.Resize(int, int)"/>
/// (which dispatches to the private <c>ResizeReflowPrimary</c> reflow path)
/// for interactive window resizes with a full scrollback ring.
/// </summary>
[MemoryDiagnoser]
public class TerminalBufferBenchmarks
{
    private const int Cols = 120;
    private const int Rows = 40;
    private const int ScrollbackLimit = 5_000;

    private static readonly string[] AsciiClusters = BuildAsciiClusters();
    private static readonly string[] WideClusters =
    {
        "\U0001F600",
        "\U0001F44B\U0001F3FD",
        "\U0001F468\u200D\U0001F4BB",
        "\u4E2D",
        "\u6587",
    };

    private TerminalBuffer filledBuffer = null!;
    private int resizeTick;

    /// <summary>
    /// Gets or sets the approximate number of rows worth of output written
    /// per iteration of the cluster-oriented benchmarks.
    /// </summary>
    [Params(1_000, 10_000)]
    public int Rowcount { get; set; }

    /// <summary>
    /// Builds a scratch buffer pre-filled to the scrollback limit so the
    /// resize benchmarks see a realistic amount of historical data.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.filledBuffer = new TerminalBuffer(Cols, Rows)
        {
            ScrollbackLimit = ScrollbackLimit,
        };

        FillToScrollback(this.filledBuffer);
    }

    /// <summary>
    /// Sustained ASCII writes through <see cref="TerminalBuffer.PutCluster(string, int)"/>.
    /// </summary>
    /// <returns>The number of clusters written.</returns>
    [Benchmark(Baseline = true)]
    public int PutClusterAscii()
    {
        var buffer = new TerminalBuffer(Cols, Rows)
        {
            ScrollbackLimit = ScrollbackLimit,
        };

        int total = this.Rowcount * Cols;
        int idx = 0;
        for (int i = 0; i < total; i++)
        {
            buffer.PutCluster(AsciiClusters[idx], 1);
            idx++;
            if (idx == AsciiClusters.Length)
            {
                idx = 0;
            }
        }

        return total;
    }

    /// <summary>
    /// Sustained writes of multi-codepoint / double-width grapheme clusters.
    /// </summary>
    /// <returns>The number of clusters written.</returns>
    [Benchmark]
    public int PutClusterWide()
    {
        var buffer = new TerminalBuffer(Cols, Rows)
        {
            ScrollbackLimit = ScrollbackLimit,
        };

        // Wide clusters take two cells; half the count keeps byte volume comparable.
        int total = (this.Rowcount * Cols) / 2;
        int idx = 0;
        for (int i = 0; i < total; i++)
        {
            string cluster = WideClusters[idx];
            buffer.PutCluster(cluster, 2);
            idx++;
            if (idx == WideClusters.Length)
            {
                idx = 0;
            }
        }

        return total;
    }

    /// <summary>
    /// Fills a fresh buffer to the scrollback limit and then resizes it to a
    /// sequence of different geometries, exercising
    /// <c>ResizeReflowPrimary</c> with a full history ring.
    /// </summary>
    /// <returns>The final column count after resizing.</returns>
    [Benchmark]
    public int ResizeReflow()
    {
        // Resize operates in-place; alternate geometries so every iteration
        // actually reflows rather than returning immediately.
        var buffer = this.filledBuffer;
        int tick = this.resizeTick++;

        int newCols = (tick & 1) == 0 ? 80 : 160;
        int newRows = (tick & 2) == 0 ? 24 : 60;
        buffer.Resize(newCols, newRows);

        // Restore the baseline geometry for the next iteration.
        buffer.Resize(Cols, Rows);
        return buffer.Cols;
    }

    private static string[] BuildAsciiClusters()
    {
        var result = new string[95];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ((char)(' ' + i)).ToString();
        }

        return result;
    }

    private static void FillToScrollback(TerminalBuffer buffer)
    {
        // Write (Rows + ScrollbackLimit) rows of content so the scrollback
        // ring fills before we measure the resize.
        int rowsToWrite = buffer.Rows + ScrollbackLimit;
        ReadOnlySpan<string> clusters = AsciiClusters;
        for (int r = 0; r < rowsToWrite; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                buffer.PutCluster(clusters[(r + c) % clusters.Length], 1);
            }

            buffer.CarriageReturn();
            buffer.LineFeed();
        }
    }
}
