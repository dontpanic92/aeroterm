// <copyright file="ISettingsPageLifecycle.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

/// <summary>
/// Optional lifecycle hook for settings pages that keep staged state until
/// the dialog is accepted.
/// </summary>
internal interface ISettingsPageLifecycle
{
    /// <summary>
    /// Commits any staged values into their backing store.
    /// </summary>
    void Commit();
}
