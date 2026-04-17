// <copyright file="ProfileStoreData.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Immutable snapshot returned by <see cref="ProfileStore.Load"/>.
/// </summary>
public sealed class ProfileStoreData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileStoreData"/> class.
    /// </summary>
    /// <param name="profiles">The profile list.</param>
    /// <param name="defaultProfileId">The default profile id, or <c>null</c>.</param>
    public ProfileStoreData(IReadOnlyList<Profile> profiles, string? defaultProfileId)
    {
        this.Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        this.DefaultProfileId = defaultProfileId;
    }

    /// <summary>
    /// Gets the profile list in display order.
    /// </summary>
    public IReadOnlyList<Profile> Profiles { get; }

    /// <summary>
    /// Gets the id of the default profile (or <c>null</c> if the list is empty).
    /// </summary>
    public string? DefaultProfileId { get; }

    /// <summary>
    /// Gets the resolved default profile, or <c>null</c> when the list is
    /// empty. Honours <see cref="DefaultProfileId"/> when it points at an
    /// existing entry; otherwise falls back to the first profile.
    /// </summary>
    public Profile? DefaultProfile
    {
        get
        {
            if (this.DefaultProfileId is not null)
            {
                foreach (var p in this.Profiles)
                {
                    if (p.Id == this.DefaultProfileId)
                    {
                        return p;
                    }
                }
            }

            return this.Profiles.Count > 0 ? this.Profiles[0] : null;
        }
    }
}
