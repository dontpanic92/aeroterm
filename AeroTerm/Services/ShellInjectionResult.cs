// <copyright file="ShellInjectionResult.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Result of attempting to inject AeroTerm shell integration into a
/// child shell launch. <see cref="ShellIntegrationInjector.Inject"/>
/// returns this so callers can replace the original <c>command</c> /
/// <c>args</c> / environment with the modified ones in one go.
/// </summary>
/// <param name="Command">Shell executable to launch.</param>
/// <param name="Args">Argument list (excluding argv[0]).</param>
/// <param name="Env">Environment dictionary to apply.</param>
/// <param name="Injected"><see langword="true"/> if injection
/// successfully modified at least one of <c>Args</c> or <c>Env</c>.</param>
internal readonly record struct ShellInjectionResult(
    string Command,
    string[] Args,
    IDictionary<string, string> Env,
    bool Injected);
