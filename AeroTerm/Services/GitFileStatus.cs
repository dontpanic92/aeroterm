// <copyright file="GitFileStatus.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// One changed path from <c>git status --porcelain=v2 --branch</c>.
/// </summary>
/// <param name="Path">Path relative to the repository root.</param>
/// <param name="IndexStatus">Index status code.</param>
/// <param name="WorkTreeStatus">Working-tree status code.</param>
/// <param name="Bucket">Status bucket for display and actions.</param>
internal sealed record GitFileStatus(
    string Path,
    char IndexStatus,
    char WorkTreeStatus,
    GitStatusBucket Bucket)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.IndexStatus}{this.WorkTreeStatus} {this.Path}";
    }
}
