// <copyright file="AvaloniaNativeMenuAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMenus;

using System.Collections.Generic;
using AeroTerm.Theme.Controls;
using Avalonia.Controls;
using ThemeNativeMenuItem = AeroTerm.Theme.Controls.NativeMenuItem;
using ThemeNativeMenuItemBase = AeroTerm.Theme.Controls.NativeMenuItemBase;

/// <summary>
/// Avalonia-backed menu adapter used on Windows, Linux, and unsupported
/// native-menu paths.
/// </summary>
internal sealed class AvaloniaNativeMenuAdapter : INativeMenuPlatformAdapter
{
    private readonly Dictionary<NativeMenuFlyout, MenuFlyout> activeMenus = new();

    /// <inheritdoc/>
    public bool ShowAt(NativeMenuFlyout flyout, Control target, bool showAtPointer)
    {
        this.Hide(flyout);

        var menu = new WidthAwareMenuFlyout
        {
            MinPresenterWidth = flyout.MinPresenterWidth,
        };
        foreach (ThemeNativeMenuItemBase item in flyout.Items)
        {
            if (CreateAvaloniaItem(item) is { } avaloniaItem)
            {
                menu.Items.Add(avaloniaItem);
            }
        }

        this.activeMenus[flyout] = menu;
        menu.Closed += (_, _) =>
        {
            this.activeMenus.Remove(flyout);
            flyout.NotifyClosed();
        };
        menu.ShowAt(target, showAtPointer);
        return true;
    }

    /// <inheritdoc/>
    public bool Hide(NativeMenuFlyout flyout)
    {
        if (!this.activeMenus.Remove(flyout, out MenuFlyout? menu))
        {
            return false;
        }

        menu.Hide();
        return true;
    }

    /// <summary>
    /// Converts a wrapper item into an Avalonia menu item.
    /// </summary>
    /// <param name="item">The wrapper item.</param>
    /// <returns>The Avalonia menu item, or <c>null</c> for hidden items.</returns>
    internal static object? CreateAvaloniaItem(ThemeNativeMenuItemBase item)
    {
        return item switch
        {
            NativeMenuSeparator => new Separator(),
            ThemeNativeMenuItem menuItem when menuItem.IsVisible => CreateAvaloniaMenuItem(menuItem),
            _ => null,
        };
    }

    private static MenuItem CreateAvaloniaMenuItem(ThemeNativeMenuItem item)
    {
        var avaloniaItem = new MenuItem
        {
            Header = item.Header,
            Icon = item.Icon,
            InputGesture = item.Gesture,
            IsEnabled = item.CanInvoke || item.Items.Count > 0,
            ToggleType = item.ToggleType,
            IsChecked = item.IsChecked,
            GroupName = item.GroupName,
        };

        foreach (ThemeNativeMenuItemBase child in item.Items)
        {
            if (CreateAvaloniaItem(child) is { } avaloniaChild)
            {
                avaloniaItem.Items.Add(avaloniaChild);
            }
        }

        if (item.Items.Count == 0)
        {
            avaloniaItem.Click += (_, _) =>
            {
                item.Invoke();
                avaloniaItem.IsChecked = item.IsChecked;
            };
        }

        return avaloniaItem;
    }

    /// <summary>
    /// Menu flyout variant that forwards a minimum presenter width to the
    /// generated <see cref="MenuFlyoutPresenter"/>. Used so dropdown popups match
    /// the dropdown button width on Avalonia-rendered platforms.
    /// </summary>
    private sealed class WidthAwareMenuFlyout : MenuFlyout
    {
        /// <summary>
        /// Gets or sets the minimum width applied to the menu's presenter.
        /// </summary>
        public double? MinPresenterWidth { get; set; }

        /// <inheritdoc/>
        protected override Control CreatePresenter()
        {
            Control presenter = base.CreatePresenter();
            if (this.MinPresenterWidth is double w && w > 0)
            {
                presenter.MinWidth = w;
            }

            return presenter;
        }
    }
}
