// <copyright file="GitDiffHighlightSet.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Side-specific highlight ranges parsed from a unified diff.
/// </summary>
/// <param name="OldRanges">Ranges highlighted on the old side.</param>
/// <param name="NewRanges">Ranges highlighted on the new side.</param>
internal sealed record GitDiffHighlightSet(
    IReadOnlyList<GitDiffHighlightRange> OldRanges,
    IReadOnlyList<GitDiffHighlightRange> NewRanges);
