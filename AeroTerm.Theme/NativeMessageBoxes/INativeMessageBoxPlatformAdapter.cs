// <copyright file="INativeMessageBoxPlatformAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System.Threading.Tasks;
using AeroTerm.Theme.Controls;
using Avalonia.Controls;

/// <summary>
/// Platform-specific native message-box presenter.
/// </summary>
internal interface INativeMessageBoxPlatformAdapter
{
    /// <summary>
    /// Shows the message box.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    /// <param name="options">The message-box options.</param>
    /// <returns>The selected result.</returns>
    Task<NativeMessageBoxResult> ShowAsync(Window owner, NativeMessageBoxOptions options);
}
