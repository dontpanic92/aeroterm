// <copyright file="TerminalInputHandlerTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Controls;
using AeroTerm.Pty;
using Avalonia.Input;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="TerminalInputHandler"/>.
/// </summary>
public class TerminalInputHandlerTests
{
    /// <summary>
    /// Mouse tracking is required before wheel events are forwarded to the PTY.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_MouseTrackingDisabled_ReturnsFalse()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)));

        bool handled = handler.HandlePointerWheelDelta(1.0, KeyModifiers.None, row: 5, col: 7);

        Assert.That(handled, Is.False);
        Assert.That(writes, Is.Empty);
    }

    /// <summary>
    /// Fractional trackpad deltas are consumed but do not emit one mouse
    /// report per input event.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_FractionalTrackpadDeltas_AccumulateBeforeReporting()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)))
        {
            MouseTrackingMode = MouseTrackingMode.Normal,
        };

        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(writes, Is.Empty);

        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.True);

        Assert.That(writes, Is.EqualTo(new[] { "\x1B[<64;7;5M" }));
    }

    /// <summary>
    /// Negative accumulated deltas emit wheel-down reports.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_NegativeDeltas_ReportWheelDown()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)))
        {
            MouseTrackingMode = MouseTrackingMode.Normal,
        };

        Assert.That(handler.HandlePointerWheelDelta(-0.5, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(handler.HandlePointerWheelDelta(-0.5, KeyModifiers.None, row: 5, col: 7), Is.True);

        Assert.That(writes, Is.EqualTo(new[] { "\x1B[<65;7;5M" }));
    }

    /// <summary>
    /// Direction changes reset stale sub-step movement from the prior direction.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_DirectionChange_ResetsRemainder()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)))
        {
            MouseTrackingMode = MouseTrackingMode.Normal,
        };

        Assert.That(handler.HandlePointerWheelDelta(0.75, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(handler.HandlePointerWheelDelta(-0.5, KeyModifiers.None, row: 5, col: 7), Is.True);
        Assert.That(writes, Is.Empty);

        Assert.That(handler.HandlePointerWheelDelta(-0.5, KeyModifiers.None, row: 5, col: 7), Is.True);

        Assert.That(writes, Is.EqualTo(new[] { "\x1B[<65;7;5M" }));
    }

    /// <summary>
    /// A full mouse-wheel detent still emits one report immediately.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_WholeDetent_ReportsImmediately()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)))
        {
            MouseTrackingMode = MouseTrackingMode.Normal,
        };

        Assert.That(handler.HandlePointerWheelDelta(1.0, KeyModifiers.Shift, row: 5, col: 7), Is.True);

        Assert.That(writes, Is.EqualTo(new[] { "\x1B[<68;7;5M" }));
    }

    /// <summary>
    /// Disabling mouse tracking clears retained movement so it is not replayed later.
    /// </summary>
    [Test]
    public void HandlePointerWheelDelta_MouseTrackingDisabled_ClearsRemainder()
    {
        var writes = new List<string>();
        var handler = new TerminalInputHandler(bytes => writes.Add(Encoding.UTF8.GetString(bytes)))
        {
            MouseTrackingMode = MouseTrackingMode.Normal,
        };

        Assert.That(handler.HandlePointerWheelDelta(0.75, KeyModifiers.None, row: 5, col: 7), Is.True);
        handler.MouseTrackingMode = MouseTrackingMode.None;
        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.False);

        handler.MouseTrackingMode = MouseTrackingMode.Normal;
        Assert.That(handler.HandlePointerWheelDelta(0.25, KeyModifiers.None, row: 5, col: 7), Is.True);

        Assert.That(writes, Is.Empty);
    }
}
