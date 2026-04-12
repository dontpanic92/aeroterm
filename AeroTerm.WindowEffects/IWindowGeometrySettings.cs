// <copyright file="IWindowGeometrySettings.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

/// <summary>
/// Defines the settings required for persisting and restoring window geometry.
/// </summary>
public interface IWindowGeometrySettings
{
    /// <summary>
    /// Gets or sets the window width in pixels.
    /// </summary>
    int WindowWidth { get; set; }

    /// <summary>
    /// Gets or sets the window height in pixels.
    /// </summary>
    int WindowHeight { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is maximized.
    /// </summary>
    bool IsMaximized { get; set; }
}
