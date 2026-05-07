// <copyright file="WheelDeltaAccumulator.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Converts high-resolution wheel deltas into whole terminal scroll steps
/// while retaining sub-step movement between input events.
/// </summary>
internal sealed class WheelDeltaAccumulator
{
    private double remainder;

    /// <summary>
    /// Gets the retained fractional step remainder.
    /// </summary>
    internal double Remainder => this.remainder;

    /// <summary>
    /// Adds a wheel delta and returns the whole number of steps now ready
    /// to apply.
    /// </summary>
    /// <param name="delta">The source wheel delta.</param>
    /// <param name="scale">The number of terminal steps represented by
    /// one source wheel delta unit.</param>
    /// <returns>The whole signed step count to apply.</returns>
    public int Add(double delta, double scale)
    {
        if (delta == 0 || scale <= 0 || !double.IsFinite(delta) || !double.IsFinite(scale))
        {
            return 0;
        }

        double scaled = delta * scale;
        if (scaled == 0 || !double.IsFinite(scaled))
        {
            return 0;
        }

        if (this.remainder != 0 && Math.Sign(scaled) != Math.Sign(this.remainder))
        {
            this.Reset();
        }

        double total = this.remainder + scaled;
        int steps = (int)total;
        this.remainder = total - steps;
        return steps;
    }

    /// <summary>
    /// Clears any retained fractional wheel movement.
    /// </summary>
    public void Reset()
    {
        this.remainder = 0;
    }
}
