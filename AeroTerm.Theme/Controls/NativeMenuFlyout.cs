// <copyright file="NativeMenuFlyout.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;
using AeroTerm.Theme.NativeMenus;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

/// <summary>
/// Flyout menu wrapper that uses AppKit menus on macOS and falls back to
/// Avalonia menus on Windows and Linux.
/// </summary>
public class NativeMenuFlyout : PopupFlyoutBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeMenuFlyout"/> class.
    /// </summary>
    public NativeMenuFlyout()
    {
        this.Items = new AvaloniaList<NativeMenuItemBase>();
    }

    /// <summary>
    /// Gets the items displayed by the menu.
    /// </summary>
    [Content]
    public AvaloniaList<NativeMenuItemBase> Items { get; }

    /// <inheritdoc/>
    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        return NativeMenuPlatformAdapter.Current.ShowAt(this, placementTarget, showAtPointer);
    }

    /// <inheritdoc/>
    protected override bool HideCore(bool canCancel)
    {
        _ = canCancel;
        return NativeMenuPlatformAdapter.Current.Hide(this);
    }

    /// <inheritdoc/>
    protected override Control CreatePresenter()
    {
        return new MenuFlyoutPresenter();
    }
}
