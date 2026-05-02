// <copyright file="NativeMenuFlyout.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;
using System.Runtime.InteropServices;
using AeroTerm.Theme.NativeMenus;
using Avalonia;
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
    private const string SplitButtonNativeMenuOpenClass = "native-menu-open";

    private Control? placementTarget;

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

    /// <summary>
    /// Clears native menu visual state when an adapter observes the backing
    /// platform menu closing outside <see cref="HideCore"/>.
    /// </summary>
    internal void NotifyClosed()
    {
        this.SetPlacementTarget(null);
    }

    /// <inheritdoc/>
    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        this.SetPlacementTarget(placementTarget);
        bool shown = NativeMenuPlatformAdapter.Current.ShowAt(this, placementTarget, showAtPointer);
        if (!shown || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            this.SetPlacementTarget(null);
        }

        return shown;
    }

    /// <inheritdoc/>
    protected override bool HideCore(bool canCancel)
    {
        _ = canCancel;
        bool hidden = NativeMenuPlatformAdapter.Current.Hide(this);
        this.SetPlacementTarget(null);
        return hidden;
    }

    /// <inheritdoc/>
    protected override Control CreatePresenter()
    {
        return new MenuFlyoutPresenter();
    }

    private void SetPlacementTarget(Control? target)
    {
        if (ReferenceEquals(this.placementTarget, target))
        {
            return;
        }

        this.SetNativeMenuOpenClass(this.placementTarget, isOpen: false);
        this.placementTarget = target;
        this.SetNativeMenuOpenClass(this.placementTarget, isOpen: true);
    }

    private void SetNativeMenuOpenClass(Control? target, bool isOpen)
    {
        if (target is not SplitButton splitButton)
        {
            return;
        }

        if (isOpen)
        {
            splitButton.Classes.Add(SplitButtonNativeMenuOpenClass);
        }
        else
        {
            splitButton.Classes.Remove(SplitButtonNativeMenuOpenClass);
        }

        splitButton.InvalidateVisual();
    }
}
