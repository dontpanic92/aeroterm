// <copyright file="SettingsPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

/// <summary>
/// Base class for settings page view models.
/// </summary>
public abstract class SettingsPageViewModel
{
    /// <summary>
    /// Gets the display name shown in the page list.
    /// </summary>
    public abstract string DisplayName { get; }
}
