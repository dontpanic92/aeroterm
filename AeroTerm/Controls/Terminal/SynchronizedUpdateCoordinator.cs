// <copyright file="SynchronizedUpdateCoordinator.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using AeroTerm.Pty;

/// <summary>
/// Coordinates DECSET 2026 synchronized-update semantics between the
/// <see cref="TerminalBuffer"/> (which flips on/off as the parser sees
/// <c>CSI ? 2026 h/l</c>) and the redraw path. While synchronized output
/// is enabled, redraws are assumed to be suppressed by the reader/bridge;
/// this coordinator (a) schedules a single redraw when the mode flips off,
/// and (b) runs a <see cref="Timer"/>-based watchdog that force-ends the
/// mode after a configurable timeout so a buggy emitter cannot stall the UI.
/// </summary>
internal sealed class SynchronizedUpdateCoordinator : IDisposable
{
    /// <summary>
    /// Default watchdog timeout matching the de-facto industry behaviour
    /// (kitty, WezTerm, iTerm2 all use ~150 ms).
    /// </summary>
    public static readonly TimeSpan DefaultWatchdogTimeout = TimeSpan.FromMilliseconds(150);

    private readonly TerminalBuffer buffer;
    private readonly Action scheduleRedraw;
    private readonly TimeSpan watchdogTimeout;
    private readonly Timer watchdog;
    private readonly object sync = new();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SynchronizedUpdateCoordinator"/> class.
    /// </summary>
    /// <param name="buffer">The terminal buffer whose synchronized-output flag drives the coordinator.</param>
    /// <param name="scheduleRedraw">Callback invoked (possibly off the UI thread) when a redraw should be enqueued. Implementations are expected to marshal to the UI thread themselves.</param>
    /// <param name="watchdogTimeout">Optional watchdog timeout; defaults to <see cref="DefaultWatchdogTimeout"/>.</param>
    public SynchronizedUpdateCoordinator(TerminalBuffer buffer, Action scheduleRedraw, TimeSpan? watchdogTimeout = null)
    {
        this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        this.scheduleRedraw = scheduleRedraw ?? throw new ArgumentNullException(nameof(scheduleRedraw));
        this.watchdogTimeout = watchdogTimeout ?? DefaultWatchdogTimeout;
        this.watchdog = new Timer(this.OnWatchdogFired, null, Timeout.Infinite, Timeout.Infinite);
        this.buffer.SynchronizedOutputChanged += this.OnSynchronizedOutputChanged;
    }

    /// <summary>
    /// Raised after the watchdog auto-ends synchronized-update mode. Intended
    /// for diagnostics/tests.
    /// </summary>
    public event EventHandler? WatchdogFired;

    /// <summary>
    /// Gets the watchdog timeout in effect for this coordinator.
    /// </summary>
    public TimeSpan WatchdogTimeout => this.watchdogTimeout;

    /// <inheritdoc />
    public void Dispose()
    {
        lock (this.sync)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.buffer.SynchronizedOutputChanged -= this.OnSynchronizedOutputChanged;
            this.watchdog.Dispose();
        }
    }

    private void OnSynchronizedOutputChanged(object? sender, bool enabled)
    {
        lock (this.sync)
        {
            if (this.disposed)
            {
                return;
            }

            if (enabled)
            {
                // (Re)arm the watchdog. A fresh "begin" while already armed
                // extends the deadline, matching kitty/WezTerm behaviour.
                this.watchdog.Change(this.watchdogTimeout, Timeout.InfiniteTimeSpan);
                return;
            }

            // Mode flipped off (either by the emitter sending CSI ? 2026 l
            // or by our own watchdog) — stop the timer and flush one redraw.
            this.watchdog.Change(Timeout.Infinite, Timeout.Infinite);
        }

        this.scheduleRedraw();
    }

    private void OnWatchdogFired(object? state)
    {
        lock (this.sync)
        {
            if (this.disposed || !this.buffer.SynchronizedOutput)
            {
                return;
            }
        }

        // Force-end synchronized mode. The setter raises
        // SynchronizedOutputChanged(false) which brings us back through
        // OnSynchronizedOutputChanged and schedules the redraw.
        this.buffer.SynchronizedOutput = false;
        this.WatchdogFired?.Invoke(this, EventArgs.Empty);
    }
}
