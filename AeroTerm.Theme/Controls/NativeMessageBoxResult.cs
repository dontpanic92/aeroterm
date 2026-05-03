// <copyright file="NativeMessageBoxResult.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

/// <summary>
/// Result returned by <see cref="NativeMessageBox"/>.
/// </summary>
public enum NativeMessageBoxResult
{
    /// <summary>
    /// The OK button was selected.
    /// </summary>
    Ok,

    /// <summary>
    /// The Yes button was selected.
    /// </summary>
    Yes,

    /// <summary>
    /// The No button, cancel gesture, or window close affordance was selected.
    /// </summary>
    No,
}
