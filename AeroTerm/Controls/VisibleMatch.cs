// <copyright file="VisibleMatch.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

/// <summary>
/// A search match projected onto the currently-rendered screen. Unlike
/// <see cref="SearchMatch"/> — whose rows are in absolute (scrollback +
/// live) coordinates — this struct carries a visible-screen row index
/// ready for the renderer.
/// </summary>
/// <param name="ScreenRow">0-based row index within the composed
/// <see cref="Pty.Screen"/> being rendered.</param>
/// <param name="StartCol">Starting column (0-based).</param>
/// <param name="CellLength">Column span (wide glyph aware).</param>
/// <param name="IsActive">Whether this is the currently-focused match;
/// drawn with a stronger tint and border than inactive matches.</param>
internal readonly record struct VisibleMatch(int ScreenRow, int StartCol, int CellLength, bool IsActive);
