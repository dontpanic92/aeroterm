// <copyright file="UpdateChannel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Specifies the update distribution channel.
/// </summary>
public enum UpdateChannel
{
    /// <summary>
    /// Stable releases published from GitHub Releases.
    /// </summary>
    Stable = 0,

    /// <summary>
    /// CI builds published to GitHub Pages on every push to master.
    /// </summary>
    CI = 1,
}
