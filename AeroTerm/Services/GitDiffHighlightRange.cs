// <copyright file="GitDiffHighlightRange.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// A one-based contiguous line range highlighted in one side of a full-file Git comparison.
/// </summary>
/// <param name="StartLine">One-based first line in the displayed document.</param>
/// <param name="LineCount">Number of lines in the range.</param>
/// <param name="Kind">Kind of change represented by the range.</param>
internal sealed record GitDiffHighlightRange(
    int StartLine,
    int LineCount,
    GitDiffHighlightKind Kind);
