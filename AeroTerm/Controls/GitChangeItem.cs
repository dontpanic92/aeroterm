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
    /// <summary>
    /// Gets the filename displayed in the compact changes list.
    /// </summary>
    internal string FileName
    {
        get
        {
            var separator = this.Status.Path.LastIndexOf('/');
            return separator >= 0 ? this.Status.Path[(separator + 1)..] : this.Status.Path;
        }
    }

    /// <summary>
    /// Gets the repository-relative parent path used for disambiguation.
    /// </summary>
    internal string ParentPath
    {
        get
        {
            var separator = this.Status.Path.LastIndexOf('/');
            return separator > 0 ? this.Status.Path[..separator] : string.Empty;
        }
    }

    /// <summary>
    /// Gets the compact Git status badge.
    /// </summary>
    internal string StatusBadge
    {
        get
        {
            if (this.Status.IndexStatus == 'U' || this.Status.WorkTreeStatus == 'U')
            {
                return "!";
            }

            if (this.Status.Bucket == GitStatusBucket.Untracked)
            {
                return "U";
            }

            var status = this.Status.Bucket == GitStatusBucket.Staged
                ? this.Status.IndexStatus
                : this.Status.WorkTreeStatus;
            return status switch
            {
                'A' => "A",
                'D' => "D",
                'R' => "R",
                'C' => "C",
                'T' => "T",
                _ => "M",
            };
        }
    }

    /// <summary>
    /// Gets the accessible description of the change.
    /// </summary>
    internal string AccessibleName =>
        $"{this.FileName}, {this.BucketLabel}, status {this.StatusBadge}, {this.Status.Path}";

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.FileName;
    }
}
