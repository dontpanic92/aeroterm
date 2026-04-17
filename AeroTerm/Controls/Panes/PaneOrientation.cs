// <copyright file="PaneOrientation.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

/// <summary>
/// The visual orientation of a <see cref="PaneSplit"/>'s divider.
/// </summary>
public enum PaneOrientation
{
    /// <summary>
    /// A horizontal divider separates the two children — they stack
    /// vertically (first on top, second on bottom). Produced by a
    /// "split horizontally" command.
    /// </summary>
    Horizontal,

    /// <summary>
    /// A vertical divider separates the two children — they sit
    /// side-by-side (first on left, second on right). Produced by a
    /// "split vertically" command.
    /// </summary>
    Vertical,
}
