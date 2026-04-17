// <copyright file="SettingsPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.Generic;

/// <summary>
/// Base class for settings page view models.
/// </summary>
public abstract class SettingsPageViewModel
{
    /// <summary>
    /// Gets the display name shown in the page list.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Gets the list of searchable labels for fields on this page.
    /// Used by the settings search box to decide whether a page should
    /// remain in the sidebar when the query is non-empty. Each string
    /// should match the <c>SettingsSearch.Label</c> attached property on
    /// the corresponding XAML row.
    /// </summary>
    public virtual IReadOnlyList<string> SearchableLabels { get; } = System.Array.Empty<string>();
}
