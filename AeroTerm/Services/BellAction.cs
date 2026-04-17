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
    None = 0,

    /// <summary>
    /// Briefly flash the window to indicate the bell.
    /// </summary>
    Visual = 1,

    /// <summary>
    /// Play the platform default beep.
    /// </summary>
    Audio = 2,

    /// <summary>
    /// Post an OS notification.
    /// </summary>
    Notification = 3,

    /// <summary>
    /// Do all of the above (visual flash, audio beep, and OS notification).
    /// </summary>
    All = 4,

    /// <summary>
    /// Visual flash and audio beep, but no OS notification. Handy when the
    /// user wants an obvious in-app cue without interrupting other apps.
    /// </summary>
    VisualAndAudio = 5,
}
