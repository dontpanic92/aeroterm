// <copyright file="PaneSplit.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

/// <summary>
/// A binary split node in the pane tree. Holds an orientation, two
/// child nodes (<see cref="First"/> / <see cref="Second"/>), and a
/// <see cref="Ratio"/> in <c>[0,1]</c> describing how much of the
/// available extent goes to the first child.
/// </summary>
public sealed class PaneSplit : PaneNode
{
    private double ratio;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaneSplit"/> class.
    /// </summary>
    /// <param name="orientation">Divider orientation.</param>
    /// <param name="first">First child (left / top).</param>
    /// <param name="second">Second child (right / bottom).</param>
    /// <param name="ratio">Initial ratio; clamped to <c>[0.05, 0.95]</c>.</param>
    internal PaneSplit(PaneOrientation orientation, PaneNode first, PaneNode second, double ratio = 0.5)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        this.Orientation = orientation;
        this.First = first;
        this.Second = second;
        this.Ratio = ratio;
        first.Parent = this;
        second.Parent = this;
    }

    /// <summary>
    /// Gets the divider orientation.
    /// </summary>
    public PaneOrientation Orientation { get; }

    /// <summary>
    /// Gets the first (left / top) child.
    /// </summary>
    public PaneNode First { get; internal set; }

    /// <summary>
    /// Gets the second (right / bottom) child.
    /// </summary>
    public PaneNode Second { get; internal set; }

    /// <summary>
    /// Gets or sets the share of the available space granted to
    /// <see cref="First"/>. Values outside <c>[0.05, 0.95]</c> are
    /// clamped to keep both panes at least a sliver wide.
    /// </summary>
    public double Ratio
    {
        get => this.ratio;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                this.ratio = 0.5;
                return;
            }

            if (value < 0.05)
            {
                this.ratio = 0.05;
            }
            else if (value > 0.95)
            {
                this.ratio = 0.95;
            }
            else
            {
                this.ratio = value;
            }
        }
    }
}
