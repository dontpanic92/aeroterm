// <copyright file="BellAction.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// How the application reacts when the terminal BEL (0x07) arrives.
/// </summary>
public enum BellAction
{
    /// <summary>
    /// Ignore the bell.
    /// </summary>
    None,

    /// <summary>
    /// Briefly flash the window to indicate the bell.
    /// </summary>
    Visual,

    /// <summary>
    /// Play the platform default beep.
    /// </summary>
    Audio,

    /// <summary>
    /// Post an OS notification.
    /// </summary>
    Notification,

    /// <summary>
    /// Do all of the above.
    /// </summary>
    All,
}
