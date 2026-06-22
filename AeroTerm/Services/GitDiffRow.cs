// <copyright file="GitDiffRow.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// One aligned row of a side-by-side diff. Both sides are always present so the
/// old and new columns stay row-aligned; the opposite side is <see langword="null"/>
/// for additions and removals.
/// </summary>
/// <param name="OldLineNumber">Line number in the old version, or <see langword="null"/>.</param>
/// <param name="OldText">Text in the old version, or <see langword="null"/>.</param>
/// <param name="NewLineNumber">Line number in the new version, or <see langword="null"/>.</param>
/// <param name="NewText">Text in the new version, or <see langword="null"/>.</param>
/// <param name="Kind">The kind of change this row represents.</param>
internal sealed record GitDiffRow(
    int? OldLineNumber,
    string? OldText,
    int? NewLineNumber,
    string? NewText,
    GitDiffRowKind Kind);
