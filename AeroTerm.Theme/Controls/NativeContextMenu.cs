// <copyright file="NativeContextMenu.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

/// <summary>
/// Context menu wrapper that opens a <see cref="NativeMenuFlyout"/> for
/// right-click pointer input.
/// </summary>
public class NativeContextMenu : NativeMenuFlyout
{
    /// <summary>
    /// Defines the attached native context menu property.
    /// </summary>
    public static readonly AttachedProperty<NativeContextMenu?> MenuProperty =
        AvaloniaProperty.RegisterAttached<NativeContextMenu, Control, NativeContextMenu?>("Menu");

    static NativeContextMenu()
    {
        MenuProperty.Changed.AddClassHandler<Control>(OnMenuChanged);
    }

    /// <summary>
    /// Gets the native context menu attached to a control.
    /// </summary>
    /// <param name="element">The target control.</param>
    /// <returns>The attached context menu, if any.</returns>
    public static NativeContextMenu? GetMenu(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(MenuProperty);
    }

    /// <summary>
    /// Sets the native context menu attached to a control.
    /// </summary>
    /// <param name="element">The target control.</param>
    /// <param name="menu">The context menu to attach.</param>
    public static void SetMenu(Control element, NativeContextMenu? menu)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(MenuProperty, menu);
    }

    private static void OnMenuChanged(Control target, AvaloniaPropertyChangedEventArgs e)
    {
        target.PointerPressed -= OnTargetPointerPressed;
        if (e.NewValue is not null)
        {
            target.PointerPressed += OnTargetPointerPressed;
        }
    }

    private static void OnTargetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control target)
        {
            return;
        }

        if (!e.GetCurrentPoint(target).Properties.IsRightButtonPressed)
        {
            return;
        }

        NativeContextMenu? menu = GetMenu(target);
        if (menu is null)
        {
            return;
        }

        e.Handled = true;
        menu.ShowAt(target, showAtPointer: true);
    }
}
