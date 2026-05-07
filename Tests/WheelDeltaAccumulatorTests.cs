// <copyright file="WheelDeltaAccumulatorTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls.Terminal;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="WheelDeltaAccumulator"/>.
/// </summary>
public class WheelDeltaAccumulatorTests
{
    /// <summary>
    /// Fractional trackpad deltas accumulate until they produce a whole scroll step.
    /// </summary>
    [Test]
    public void Add_FractionalDeltas_AccumulatesUntilWholeStep()
    {
        var accumulator = new WheelDeltaAccumulator();

        Assert.That(accumulator.Add(0.2, 3.0), Is.EqualTo(0));
        Assert.That(accumulator.Remainder, Is.EqualTo(0.6).Within(0.000_001));

        Assert.That(accumulator.Add(0.2, 3.0), Is.EqualTo(1));
        Assert.That(accumulator.Remainder, Is.EqualTo(0.2).Within(0.000_001));
    }

    /// <summary>
    /// Direction changes discard stale sub-step movement from the opposite direction.
    /// </summary>
    [Test]
    public void Add_DirectionChange_ResetsOppositeRemainder()
    {
        var accumulator = new WheelDeltaAccumulator();

        Assert.That(accumulator.Add(0.25, 3.0), Is.EqualTo(0));
        Assert.That(accumulator.Remainder, Is.GreaterThan(0));

        Assert.That(accumulator.Add(-0.25, 3.0), Is.EqualTo(0));
        Assert.That(accumulator.Remainder, Is.EqualTo(-0.75).Within(0.000_001));
    }

    /// <summary>
    /// Reset clears retained trackpad movement.
    /// </summary>
    [Test]
    public void Reset_ClearsRemainder()
    {
        var accumulator = new WheelDeltaAccumulator();

        _ = accumulator.Add(0.25, 3.0);
        accumulator.Reset();

        Assert.That(accumulator.Remainder, Is.EqualTo(0));
    }

    /// <summary>
    /// Invalid input does not change the accumulator.
    /// </summary>
    [Test]
    public void Add_InvalidInput_DoesNotChangeRemainder()
    {
        var accumulator = new WheelDeltaAccumulator();

        _ = accumulator.Add(0.25, 3.0);
        Assert.That(accumulator.Add(double.NaN, 3.0), Is.EqualTo(0));
        Assert.That(accumulator.Add(0.25, double.PositiveInfinity), Is.EqualTo(0));

        Assert.That(accumulator.Remainder, Is.EqualTo(0.75).Within(0.000_001));
    }
}
