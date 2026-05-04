// <copyright file="DiscoveredShell.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// One discovered shell entry. Maps directly to a fresh
/// <see cref="Profile"/> when seeding <c>profiles.json</c> on first run.
/// </summary>
/// <param name="Name">Human-readable profile name (e.g. "PowerShell",
/// "Ubuntu (WSL)").</param>
/// <param name="Command">Absolute path to the shell executable, or — for
/// WSL — <c>wsl.exe</c> launched with distro args.</param>
/// <param name="Args">Argument vector to pass on launch. Always
/// non-null; empty means "no args".</param>
/// <param name="WorkingDirectory">Initial working directory, or
/// <c>null</c> to inherit the platform default.</param>
public sealed record DiscoveredShell(
    string Name,
    string Command,
    string[] Args,
    string? WorkingDirectory);
