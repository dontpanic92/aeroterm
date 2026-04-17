// <copyright file="PaneDirection.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

/// <summary>
/// Compass-style direction used by focus-pane navigation.
/// </summary>
public enum PaneDirection
{
    /// <summary>Move focus to the pane immediately to the left.</summary>
    Left,

    /// <summary>Move focus to the pane immediately to the right.</summary>
    Right,

    /// <summary>Move focus to the pane immediately above.</summary>
    Up,

    /// <summary>Move focus to the pane immediately below.</summary>
    Down,
}
