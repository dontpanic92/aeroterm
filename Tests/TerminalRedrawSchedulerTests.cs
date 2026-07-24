// <copyright file="TerminalRedrawSchedulerTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;

/// <summary>
/// Tests frame pacing and request coalescing in
/// <see cref="TerminalRedrawScheduler"/>.
/// </summary>
[TestFixture]
public sealed class TerminalRedrawSchedulerTests
{
    /// <summary>
    /// Concurrent requests collapse into one immediate redraw and one delayed
    /// redraw after the frame interval.
    /// </summary>
    [Test]
    public void Request_MultipleRequests_CoalescesAndFramePaces()
    {
        var posted = new Queue<Action>();
        var delayed = new List<(Action Callback, TimeSpan Delay)>();
        long timestamp = 0;
        int redraws = 0;
        using var scheduler = new TerminalRedrawScheduler(
            posted.Enqueue,
            (callback, delay) => delayed.Add((callback, delay)),
            () => redraws++,
            TimeSpan.FromMilliseconds(10),
            () => timestamp,
            timestampFrequency: 1000);

        scheduler.Request();
        scheduler.Request();
        scheduler.Request();

        Assert.That(posted, Has.Count.EqualTo(1));
        posted.Dequeue()();
        Assert.That(redraws, Is.EqualTo(1));

        timestamp = 3;
        scheduler.Request();
        scheduler.Request();
        Assert.That(posted, Has.Count.EqualTo(1));

        posted.Dequeue()();
        Assert.That(redraws, Is.EqualTo(1));
        Assert.That(delayed, Has.Count.EqualTo(1));
        Assert.That(delayed[0].Delay, Is.EqualTo(TimeSpan.FromMilliseconds(7)));

        timestamp = 10;
        delayed[0].Callback();
        Assert.That(redraws, Is.EqualTo(2));
    }

    /// <summary>
    /// A request arriving during a redraw is retained for the next frame.
    /// </summary>
    [Test]
    public void Request_DuringRedraw_SchedulesNextFrame()
    {
        var posted = new Queue<Action>();
        var delayed = new List<(Action Callback, TimeSpan Delay)>();
        long timestamp = 0;
        int redraws = 0;
        TerminalRedrawScheduler? scheduler = null;
        scheduler = new TerminalRedrawScheduler(
            posted.Enqueue,
            (callback, delay) => delayed.Add((callback, delay)),
            () =>
            {
                redraws++;
                if (redraws == 1)
                {
                    scheduler!.Request();
                }
            },
            TimeSpan.FromMilliseconds(10),
            () => timestamp,
            timestampFrequency: 1000);

        using (scheduler)
        {
            scheduler.Request();
            posted.Dequeue()();

            Assert.That(redraws, Is.EqualTo(1));
            Assert.That(delayed, Has.Count.EqualTo(1));

            timestamp = 10;
            delayed[0].Callback();
            Assert.That(redraws, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Delayed callbacks become no-ops after disposal.
    /// </summary>
    [Test]
    public void Dispose_WithDelayedRedraw_SuppressesCallback()
    {
        var posted = new Queue<Action>();
        var delayed = new List<(Action Callback, TimeSpan Delay)>();
        long timestamp = 0;
        int redraws = 0;
        var scheduler = new TerminalRedrawScheduler(
            posted.Enqueue,
            (callback, delay) => delayed.Add((callback, delay)),
            () => redraws++,
            TimeSpan.FromMilliseconds(10),
            () => timestamp,
            timestampFrequency: 1000);

        scheduler.Request();
        posted.Dequeue()();
        timestamp = 1;
        scheduler.Request();
        posted.Dequeue()();
        scheduler.Dispose();

        delayed[0].Callback();

        Assert.That(redraws, Is.EqualTo(1));
    }
}
