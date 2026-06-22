// <copyright file="GitDiffFile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// A single file's worth of parsed side-by-side diff rows.
/// </summary>
/// <param name="Path">Repository-relative path of the file.</param>
/// <param name="Rows">Aligned diff rows for the file.</param>
/// <param name="IsBinary">Whether Git reported the file as binary.</param>
internal sealed record GitDiffFile(
    string Path,
    IReadOnlyList<GitDiffRow> Rows,
    bool IsBinary);
