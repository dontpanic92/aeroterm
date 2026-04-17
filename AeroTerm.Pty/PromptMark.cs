// <copyright file="PromptMark.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// A single prompt mark captured in buffer space.
/// </summary>
/// <param name="Kind">Mark kind (<see cref="PromptMarkKind"/>).</param>
/// <param name="AbsoluteRow">Stable row identifier: <c>ScrollbackCount +
/// CursorRow</c> at capture time. Monotonically increasing within a
/// session; drifts once the scrollback ring saturates — see
/// <see cref="PromptMarksRegistry"/>.</param>
/// <param name="Column">Column index at capture time.</param>
/// <param name="ExitCode">Exit code captured with
/// <see cref="PromptMarkKind.CommandEnd"/>, else <see langword="null"/>.</param>
/// <param name="CurrentDirectory">Optional working-directory hint.</param>
public sealed record PromptMark(
    PromptMarkKind Kind,
    int AbsoluteRow,
    int Column,
    int? ExitCode,
    string? CurrentDirectory);
