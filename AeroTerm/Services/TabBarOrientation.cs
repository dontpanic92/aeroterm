// <copyright file="TabBarOrientation.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Orientation of the tab strip. Controls whether tabs are rendered as a
/// traditional horizontal strip across the top of the window, or as a
/// narrow vertical rail along the left edge.
/// </summary>
public enum TabBarOrientation
{
    /// <summary>
    /// Tabs render as a horizontal strip docked to the top of the window
    /// (default behaviour). Tab headers grow leftwards to rightwards.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Tabs render as a narrow vertical rail docked to the left edge of
    /// the window. Tab headers stack top-to-bottom.
    /// </summary>
    Vertical,
}
