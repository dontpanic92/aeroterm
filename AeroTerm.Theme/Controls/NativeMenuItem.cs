// <copyright file="NativeMenuItem.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Metadata;

/// <summary>
/// Command item used by <see cref="NativeMenuFlyout"/> and
/// <see cref="NativeContextMenu"/>.
/// </summary>
public class NativeMenuItem : NativeMenuItemBase
{
    /// <summary>
    /// Defines the <see cref="Header"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<NativeMenuItem, object?>(nameof(Header));

    /// <summary>
    /// Defines the <see cref="Icon"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<NativeMenuItem, object?>(nameof(Icon));

    /// <summary>
    /// Defines the <see cref="Gesture"/> property.
    /// </summary>
    public static readonly StyledProperty<KeyGesture?> GestureProperty =
        AvaloniaProperty.Register<NativeMenuItem, KeyGesture?>(nameof(Gesture));

    /// <summary>
    /// Defines the <see cref="Command"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<NativeMenuItem, ICommand?>(nameof(Command));

    /// <summary>
    /// Defines the <see cref="CommandParameter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<NativeMenuItem, object?>(nameof(CommandParameter));

    /// <summary>
    /// Defines the <see cref="IsEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsEnabledProperty =
        AvaloniaProperty.Register<NativeMenuItem, bool>(nameof(IsEnabled), defaultValue: true);

    /// <summary>
    /// Defines the <see cref="IsVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsVisibleProperty =
        AvaloniaProperty.Register<NativeMenuItem, bool>(nameof(IsVisible), defaultValue: true);

    /// <summary>
    /// Defines the <see cref="ToggleType"/> property.
    /// </summary>
    public static readonly StyledProperty<MenuItemToggleType> ToggleTypeProperty =
        AvaloniaProperty.Register<NativeMenuItem, MenuItemToggleType>(nameof(ToggleType));

    /// <summary>
    /// Defines the <see cref="IsChecked"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<NativeMenuItem, bool>(nameof(IsChecked));

    /// <summary>
    /// Defines the <see cref="GroupName"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> GroupNameProperty =
        AvaloniaProperty.Register<NativeMenuItem, string?>(nameof(GroupName));

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeMenuItem"/> class.
    /// </summary>
    public NativeMenuItem()
    {
        this.Items = new AvaloniaList<NativeMenuItemBase>();
    }

    /// <summary>
    /// Occurs when this item is invoked.
    /// </summary>
    public event EventHandler? Click;

    /// <summary>
    /// Gets child menu items displayed as a submenu.
    /// </summary>
    [Content]
    public AvaloniaList<NativeMenuItemBase> Items { get; }

    /// <summary>
    /// Gets or sets the item header.
    /// </summary>
    public object? Header
    {
        get => this.GetValue(HeaderProperty);
        set => this.SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional item icon.
    /// </summary>
    public object? Icon
    {
        get => this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the displayed input gesture.
    /// </summary>
    public KeyGesture? Gesture
    {
        get => this.GetValue(GestureProperty);
        set => this.SetValue(GestureProperty, value);
    }

    /// <summary>
    /// Gets or sets the command invoked by the item.
    /// </summary>
    public ICommand? Command
    {
        get => this.GetValue(CommandProperty);
        set => this.SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command parameter.
    /// </summary>
    public object? CommandParameter
    {
        get => this.GetValue(CommandParameterProperty);
        set => this.SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item is visible.
    /// </summary>
    public bool IsVisible
    {
        get => this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the item's toggle behavior.
    /// </summary>
    public MenuItemToggleType ToggleType
    {
        get => this.GetValue(ToggleTypeProperty);
        set => this.SetValue(ToggleTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a toggle item is checked.
    /// </summary>
    public bool IsChecked
    {
        get => this.GetValue(IsCheckedProperty);
        set => this.SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// Gets or sets the radio group name.
    /// </summary>
    public string? GroupName
    {
        get => this.GetValue(GroupNameProperty);
        set => this.SetValue(GroupNameProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether this item can currently be invoked.
    /// </summary>
    internal bool CanInvoke => this.IsEnabled
        && this.IsVisible
        && (this.Command?.CanExecute(this.CommandParameter) ?? true);

    /// <summary>
    /// Invokes the item command and click event.
    /// </summary>
    internal void Invoke()
    {
        if (!this.CanInvoke)
        {
            return;
        }

        if (this.ToggleType == MenuItemToggleType.CheckBox)
        {
            this.IsChecked = !this.IsChecked;
        }
        else if (this.ToggleType == MenuItemToggleType.Radio)
        {
            this.IsChecked = true;
        }

        this.Command?.Execute(this.CommandParameter);
        this.Click?.Invoke(this, EventArgs.Empty);
    }
}
