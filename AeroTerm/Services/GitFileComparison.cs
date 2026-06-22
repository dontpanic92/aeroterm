// <copyright file="GitFileComparison.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Full-file old/new content loaded for the Git page.
/// </summary>
/// <param name="Path">Repository-relative path being compared.</param>
/// <param name="OldSide">Old-side file content.</param>
/// <param name="NewSide">New-side file content.</param>
/// <param name="IsBinary">Whether the selected path is binary.</param>
/// <param name="ErrorMessage">User-visible error message, if loading failed.</param>
internal sealed record GitFileComparison(
    string Path,
    GitFileSideContent? OldSide,
    GitFileSideContent? NewSide,
    bool IsBinary,
    string? ErrorMessage)
{
    /// <summary>
    /// Gets a value indicating whether the comparison loaded successfully.
    /// </summary>
    internal bool Succeeded => this.ErrorMessage is null && !this.IsBinary && this.OldSide is not null && this.NewSide is not null;
}
