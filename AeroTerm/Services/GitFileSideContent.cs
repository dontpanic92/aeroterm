// <copyright file="GitFileSideContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Text and highlights for one side of a full-file Git comparison.
/// </summary>
/// <param name="Text">Displayed file text.</param>
/// <param name="SourceLabel">User-visible label describing where the text came from.</param>
/// <param name="Highlights">Line highlights for this side.</param>
internal sealed record GitFileSideContent(
    string Text,
    string SourceLabel,
    IReadOnlyList<GitDiffHighlightRange> Highlights);
