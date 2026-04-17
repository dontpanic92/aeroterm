// <copyright file="SearchMatch.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

/// <summary>
/// A single search hit expressed in terminal grid coordinates.
/// </summary>
/// <param name="AbsoluteRow">Row index in the concatenated
/// scrollback-then-live corpus passed to
/// <see cref="ScrollbackSearch.FindMatches"/>.</param>
/// <param name="StartCol">Starting column (0-based) within that row.</param>
/// <param name="CellLength">Number of terminal columns the match spans
/// (wide glyphs count as their full display width).</param>
internal readonly record struct SearchMatch(int AbsoluteRow, int StartCol, int CellLength);
