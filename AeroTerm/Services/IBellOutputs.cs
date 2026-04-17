// <copyright file="IBellOutputs.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Abstracts the three user-observable reactions to a terminal BEL so that
/// <see cref="BellDispatcher"/> can be unit-tested without any Avalonia,
/// audio, or OS-notification plumbing.
/// </summary>
internal interface IBellOutputs
{
    /// <summary>
    /// Briefly flash the window or tab border to visually indicate the bell.
    /// </summary>
    void Visual();

    /// <summary>
    /// Play the platform default beep sound.
    /// </summary>
    void Audio();

    /// <summary>
    /// Post an OS notification advertising the bell.
    /// </summary>
    void Notify();
}
