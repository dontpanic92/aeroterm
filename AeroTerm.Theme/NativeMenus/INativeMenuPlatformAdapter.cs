// <copyright file="INativeMenuPlatformAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMenus;

using AeroTerm.Theme.Controls;
using Avalonia.Controls;

/// <summary>
/// Platform-specific presenter for <see cref="NativeMenuFlyout"/>.
/// </summary>
internal interface INativeMenuPlatformAdapter
{
    /// <summary>
    /// Shows the menu at the specified target.
    /// </summary>
    /// <param name="flyout">The menu flyout wrapper.</param>
    /// <param name="target">The target control.</param>
    /// <param name="showAtPointer">Whether to prefer the current pointer location.</param>
    /// <returns><c>true</c> if the show request was handled.</returns>
    bool ShowAt(NativeMenuFlyout flyout, Control target, bool showAtPointer);

    /// <summary>
    /// Hides the menu.
    /// </summary>
    /// <param name="flyout">The menu flyout wrapper.</param>
    /// <returns><c>true</c> if the hide request was handled.</returns>
    bool Hide(NativeMenuFlyout flyout);
}
