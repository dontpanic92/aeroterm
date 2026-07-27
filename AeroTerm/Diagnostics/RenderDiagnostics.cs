// <copyright file="RenderDiagnostics.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Opt-in per-frame render counters used to locate GPU and CPU bottlenecks.
/// Disabled unless the <c>AEROTERM_RENDER_DIAG</c> environment variable is set
/// to <c>1</c>, so there is no cost in normal use.
/// </summary>
/// <remarks>
/// Counters distinguish the three stages that can independently drive the frame
/// rate: PTY redraw requests, redraw dispatches surviving frame pacing, and
/// actual canvas renders. A render count far above the dispatch count means
/// something other than terminal output is invalidating the control.
/// </remarks>
internal static class RenderDiagnostics
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(2);

    private static long redrawRequests;
    private static long redrawDispatches;
    private static long renders;
    private static long rowCacheRenders;
    private static long rowsRecorded;
    private static long insetRebuilds;
    private static long renderTicks;
    private static long maxRenderTicks;
    private static long lastReportTimestamp;
    private static int reporting;

    /// <summary>
    /// Gets a value indicating whether diagnostics collection is enabled.
    /// </summary>
    public static bool Enabled { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("AEROTERM_RENDER_DIAG"),
            "1",
            StringComparison.Ordinal);

    /// <summary>Records one redraw request from the PTY reader.</summary>
    public static void RecordRedrawRequest()
    {
        if (Enabled)
        {
            Interlocked.Increment(ref redrawRequests);
        }
    }

    /// <summary>Records one redraw dispatch that survived frame pacing.</summary>
    public static void RecordRedrawDispatch()
    {
        if (Enabled)
        {
            Interlocked.Increment(ref redrawDispatches);
        }
    }

    /// <summary>Records one title-bar inset blur rebuild.</summary>
    public static void RecordInsetRebuild()
    {
        if (Enabled)
        {
            Interlocked.Increment(ref insetRebuilds);
        }
    }

    /// <summary>
    /// Records one completed canvas render and periodically emits a summary.
    /// </summary>
    /// <param name="usedRowCache">Whether the retained row path was used.</param>
    /// <param name="recordedRows">Rows re-recorded into the row cache.</param>
    /// <param name="elapsedTicks">Render duration in stopwatch ticks.</param>
    public static void RecordRender(bool usedRowCache, int recordedRows, long elapsedTicks)
    {
        if (!Enabled)
        {
            return;
        }

        Interlocked.Increment(ref renders);
        if (usedRowCache)
        {
            Interlocked.Increment(ref rowCacheRenders);
        }

        Interlocked.Add(ref rowsRecorded, recordedRows);
        Interlocked.Add(ref renderTicks, elapsedTicks);

        long observedMax = Volatile.Read(ref maxRenderTicks);
        while (elapsedTicks > observedMax)
        {
            long previous = Interlocked.CompareExchange(ref maxRenderTicks, elapsedTicks, observedMax);
            if (previous == observedMax)
            {
                break;
            }

            observedMax = previous;
        }

        MaybeReport();
    }

    private static void MaybeReport()
    {
        long now = Stopwatch.GetTimestamp();
        long last = Volatile.Read(ref lastReportTimestamp);
        if (last == 0)
        {
            Interlocked.CompareExchange(ref lastReportTimestamp, now, 0);
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(last, now);
        if (elapsed < ReportInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref reporting, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (Interlocked.CompareExchange(ref lastReportTimestamp, now, last) != last)
            {
                return;
            }

            long frames = Interlocked.Exchange(ref renders, 0);
            long requests = Interlocked.Exchange(ref redrawRequests, 0);
            long dispatches = Interlocked.Exchange(ref redrawDispatches, 0);
            long cached = Interlocked.Exchange(ref rowCacheRenders, 0);
            long rows = Interlocked.Exchange(ref rowsRecorded, 0);
            long insets = Interlocked.Exchange(ref insetRebuilds, 0);
            long ticks = Interlocked.Exchange(ref renderTicks, 0);
            long peak = Interlocked.Exchange(ref maxRenderTicks, 0);

            double seconds = elapsed.TotalSeconds;
            double meanMs = frames == 0 ? 0 : Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds / frames;
            double peakMs = Stopwatch.GetElapsedTime(0, peak).TotalMilliseconds;

            AppLogger.For("RenderDiag").LogInformation(
                "fps={Fps:F1} renders={Renders} ptyRequests={Requests} dispatches={Dispatches} rowCache={Cached} rowsRecorded={Rows} insetRebuilds={Insets} meanRender={MeanMs:F2}ms peakRender={PeakMs:F2}ms cpuBusy={Busy:F1}%",
                frames / seconds,
                frames,
                requests,
                dispatches,
                cached,
                rows,
                insets,
                meanMs,
                peakMs,
                Stopwatch.GetElapsedTime(0, ticks).TotalSeconds / seconds * 100);
        }
        finally
        {
            Volatile.Write(ref reporting, 0);
        }
    }
}
