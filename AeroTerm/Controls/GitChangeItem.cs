// <copyright file="GitChangeItem.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Services;

/// <summary>
/// A single entry in the Git pane's combined changes list, pairing a status
/// entry with a human-readable bucket label.
/// </summary>
/// <param name="Status">The underlying Git file status.</param>
/// <param name="BucketLabel">Short bucket label (e.g. staged, changed, untracked).</param>
internal sealed record GitChangeItem(GitFileStatus Status, string BucketLabel)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"[{this.BucketLabel}] {this.Status.IndexStatus}{this.Status.WorkTreeStatus} {this.Status.Path}";
    }
}
