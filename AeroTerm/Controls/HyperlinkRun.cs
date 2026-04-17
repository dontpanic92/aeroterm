// <copyright file="HyperlinkRun.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

/// <summary>
/// A contiguous range of cells on the same row that participate in the same
/// OSC 8 hyperlink.
/// </summary>
/// <param name="Row">The row index (0-based).</param>
/// <param name="StartCol">The inclusive start column (0-based).</param>
/// <param name="EndCol">The inclusive end column (0-based).</param>
/// <param name="Uri">The hyperlink URI.</param>
/// <param name="Id">The optional hyperlink identifier, or <see langword="null"/>.</param>
internal readonly record struct HyperlinkRun(int Row, int StartCol, int EndCol, string Uri, string? Id);
