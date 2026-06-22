// <copyright file="TerminalEnvironment.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Applies terminal capability environment defaults for child processes.
/// </summary>
internal static class TerminalEnvironment
{
    private const string CopilotPromptFrameEnvVar = "COPILOT_PROMPT_FRAME";

    /// <summary>
    /// Applies AeroTerm's default terminal capability environment variables.
    /// Existing values are preserved for opt-out or caller-specific overrides.
    /// </summary>
    /// <param name="env">The environment dictionary to update.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="env"/> is <see langword="null"/>.</exception>
    public static void ApplyDefaults(IDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(env);

        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";

        if (!env.ContainsKey(CopilotPromptFrameEnvVar))
        {
            env[CopilotPromptFrameEnvVar] = "1";
        }
    }
}
