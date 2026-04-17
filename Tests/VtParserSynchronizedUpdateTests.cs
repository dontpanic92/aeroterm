// <copyright file="VtParserSynchronizedUpdateTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using System.Threading;
using AeroTerm.Controls.Terminal;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for DECSET 2026 — synchronized update mode — covering the parser
/// entry/exit handling, the DECRQM query-response shape, and the
/// <see cref="SynchronizedUpdateCoordinator"/> redraw/watchdog integration.
/// </summary>
public class VtParserSynchronizedUpdateTests
{
    /// <summary>
    /// <c>CSI ? 2026 h</c> and <c>CSI ? 2026 l</c> toggle the buffer flag
    /// and fire the <see cref="TerminalBuffer.SynchronizedOutputChanged"/>
    /// event on each transition only.
    /// </summary>
    [Test]
    public void Process_SynchronizedUpdate_TogglesAndRaisesEvent()
    {
        var buffer = new TerminalBuffer(4, 2);
        var parser = new VtParser(buffer, _ => { });
        var transitions = new List<bool>();
        buffer.SynchronizedOutputChanged += (_, enabled) => transitions.Add(enabled);

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026h"));
        Assert.That(buffer.SynchronizedOutput, Is.True);

        // Redundant enable should NOT raise the event again.
        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026h"));

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026l"));
        Assert.That(buffer.SynchronizedOutput, Is.False);

        Assert.That(transitions, Is.EqualTo(new[] { true, false }));
    }

    /// <summary>
    /// DECRQM (<c>CSI ? 2026 $ p</c>) should report <c>?2026;1$y</c> when
    /// synchronized output is currently enabled and <c>?2026;2$y</c> when
    /// disabled (we support the mode, so neither 0 nor 3/4 is valid).
    /// </summary>
    [Test]
    public void Process_DecrqmSynchronizedUpdate_ReportsSupportedState()
    {
        var buffer = new TerminalBuffer(4, 2);
        var responses = new List<string>();
        var parser = new VtParser(
            buffer,
            _ => { },
            data => responses.Add(Encoding.ASCII.GetString(data)));

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026$p"));
        Assert.That(responses, Has.Count.EqualTo(1));
        Assert.That(responses[0], Is.EqualTo("\x1B[?2026;2$y"));

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026h"));
        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026$p"));
        Assert.That(responses, Has.Count.EqualTo(2));
        Assert.That(responses[1], Is.EqualTo("\x1B[?2026;1$y"));
    }

    /// <summary>
    /// A complete begin / mutate / end sequence parsed in one go should
    /// leave the buffer with the mutations applied, the mode back off, and
    /// exactly one redraw posted through the coordinator.
    /// </summary>
    [Test]
    public void Coordinator_BeginMutateEnd_SchedulesSingleRedraw()
    {
        var buffer = new TerminalBuffer(10, 2);
        var parser = new VtParser(buffer, _ => { });

        int redraws = 0;
        using var coordinator = new SynchronizedUpdateCoordinator(
            buffer,
            () => Interlocked.Increment(ref redraws));

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026hHELLO\x1B[?2026l"));

        Assert.That(buffer.SynchronizedOutput, Is.False);
        Assert.That(redraws, Is.EqualTo(1));

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("H"));
        Assert.That(screen.Cells[0, 4].Character, Is.EqualTo("O"));
    }

    /// <summary>
    /// If the emitter never sends the terminating <c>CSI ? 2026 l</c>, the
    /// watchdog must auto-end the mode and schedule a redraw so the UI does
    /// not stall. Uses a short timeout via the coordinator's injection seam.
    /// </summary>
    [Test]
    public void Coordinator_Watchdog_ForceEndsAfterTimeout()
    {
        var buffer = new TerminalBuffer(4, 2);
        var parser = new VtParser(buffer, _ => { });

        int redraws = 0;
        using var redrawSignal = new ManualResetEventSlim(false);
        using var coordinator = new SynchronizedUpdateCoordinator(
            buffer,
            () =>
            {
                Interlocked.Increment(ref redraws);
                redrawSignal.Set();
            },
            TimeSpan.FromMilliseconds(30));

        using var watchdogFired = new ManualResetEventSlim(false);
        coordinator.WatchdogFired += (_, _) => watchdogFired.Set();

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026h"));
        Assert.That(buffer.SynchronizedOutput, Is.True);

        Assert.That(watchdogFired.Wait(TimeSpan.FromSeconds(2)), Is.True, "watchdog did not fire");
        Assert.That(redrawSignal.Wait(TimeSpan.FromSeconds(2)), Is.True, "no redraw was scheduled after watchdog");
        Assert.That(buffer.SynchronizedOutput, Is.False);
        Assert.That(redraws, Is.EqualTo(1));
    }

    /// <summary>
    /// An explicit end from the emitter should cancel the watchdog — arming
    /// the mode again after a successful clean end must not re-fire the
    /// original watchdog (the coordinator re-arms on each begin).
    /// </summary>
    [Test]
    public void Coordinator_CleanEnd_CancelsWatchdog()
    {
        var buffer = new TerminalBuffer(4, 2);
        var parser = new VtParser(buffer, _ => { });

        int redraws = 0;
        using var coordinator = new SynchronizedUpdateCoordinator(
            buffer,
            () => Interlocked.Increment(ref redraws),
            TimeSpan.FromMilliseconds(40));

        int watchdogFirings = 0;
        coordinator.WatchdogFired += (_, _) => Interlocked.Increment(ref watchdogFirings);

        parser.Process(Encoding.UTF8.GetBytes("\x1B[?2026hX\x1B[?2026l"));

        // Wait well past the watchdog timeout; it must NOT have fired.
        Thread.Sleep(120);

        Assert.That(watchdogFirings, Is.EqualTo(0));
        Assert.That(redraws, Is.EqualTo(1));
        Assert.That(buffer.SynchronizedOutput, Is.False);
    }
}
