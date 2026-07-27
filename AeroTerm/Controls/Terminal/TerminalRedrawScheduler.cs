// <copyright file="TerminalRedrawScheduler.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using System.Diagnostics;
using AeroTerm.Diagnostics;

/// <summary>
/// Coalesces redraw requests and dispatches them no faster than a configured
/// frame interval.
/// </summary>
internal sealed class TerminalRedrawScheduler : IDisposable
{
    private readonly Action<Action> post;
    private readonly Action<Action, TimeSpan> delay;
    private readonly Action redraw;
    private readonly TimeSpan minimumInterval;
    private readonly Func<long> getTimestamp;
    private readonly long timestampFrequency;
    private long lastDispatchTimestamp;
    private int hasDispatched;
    private int pending;
    private int scheduled;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalRedrawScheduler"/> class.
    /// </summary>
    /// <param name="post">Posts an action to the UI thread.</param>
    /// <param name="delay">Schedules an action on the UI thread after a delay.</param>
    /// <param name="redraw">Dispatches one redraw tick.</param>
    /// <param name="minimumInterval">Minimum interval between redraw ticks.</param>
    /// <param name="getTimestamp">Optional monotonic timestamp provider for tests.</param>
    /// <param name="timestampFrequency">Optional timestamp frequency for tests.</param>
    public TerminalRedrawScheduler(
        Action<Action> post,
        Action<Action, TimeSpan> delay,
        Action redraw,
        TimeSpan minimumInterval,
        Func<long>? getTimestamp = null,
        long? timestampFrequency = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(redraw);

        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        long resolvedTimestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (resolvedTimestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        this.post = post;
        this.delay = delay;
        this.redraw = redraw;
        this.minimumInterval = minimumInterval;
        this.getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        this.timestampFrequency = resolvedTimestampFrequency;
    }

    /// <summary>
    /// Requests a redraw. Concurrent requests are collapsed while preserving
    /// one pending redraw when content changes during a dispatch.
    /// </summary>
    public void Request()
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            return;
        }

        RenderDiagnostics.RecordRedrawRequest();
        Interlocked.Exchange(ref this.pending, 1);
        if (Interlocked.CompareExchange(ref this.scheduled, 1, 0) == 0)
        {
            this.post(this.ScheduleOrDispatch);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref this.disposed, 1);
        Interlocked.Exchange(ref this.pending, 0);
    }

    private void ScheduleOrDispatch()
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            Interlocked.Exchange(ref this.scheduled, 0);
            return;
        }

        TimeSpan remaining = this.GetRemainingDelay();
        if (remaining > TimeSpan.Zero)
        {
            this.delay(this.Dispatch, remaining);
            return;
        }

        this.Dispatch();
    }

    private void Dispatch()
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            Interlocked.Exchange(ref this.scheduled, 0);
            return;
        }

        Interlocked.Exchange(ref this.pending, 0);
        try
        {
            RenderDiagnostics.RecordRedrawDispatch();
            this.redraw();
        }
        finally
        {
            this.lastDispatchTimestamp = this.getTimestamp();
            Volatile.Write(ref this.hasDispatched, 1);
            Interlocked.Exchange(ref this.scheduled, 0);

            if (Volatile.Read(ref this.pending) != 0
                && Interlocked.CompareExchange(ref this.scheduled, 1, 0) == 0)
            {
                this.ScheduleOrDispatch();
            }
        }
    }

    private TimeSpan GetRemainingDelay()
    {
        if (Volatile.Read(ref this.hasDispatched) == 0 || this.minimumInterval == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        long elapsedTicks = Math.Max(0, this.getTimestamp() - this.lastDispatchTimestamp);
        double elapsedSeconds = (double)elapsedTicks / this.timestampFrequency;
        TimeSpan elapsed = TimeSpan.FromSeconds(elapsedSeconds);
        TimeSpan remaining = this.minimumInterval - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
