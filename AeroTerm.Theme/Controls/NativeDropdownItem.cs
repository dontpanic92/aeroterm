// <copyright file="NativeDropdownItem.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using Avalonia;
using Avalonia.Metadata;

/// <summary>
/// Item displayed by a <see cref="NativeDropdown"/>.
/// </summary>
public class NativeDropdownItem : AvaloniaObject
{
    /// <summary>
    /// Defines the <see cref="Content"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<NativeDropdownItem, object?>(nameof(Content));

    /// <summary>
    /// Defines the <see cref="Value"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<NativeDropdownItem, object?>(nameof(Value));

    /// <summary>
    /// Defines the <see cref="IsEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsEnabledProperty =
        AvaloniaProperty.Register<NativeDropdownItem, bool>(nameof(IsEnabled), defaultValue: true);

    /// <summary>
    /// Defines the <see cref="IsVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsVisibleProperty =
        AvaloniaProperty.Register<NativeDropdownItem, bool>(nameof(IsVisible), defaultValue: true);

    /// <summary>
    /// Gets or sets the displayed item content.
    /// </summary>
    [Content]
    public object? Content
    {
        get => this.GetValue(ContentProperty);
        set => this.SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected value. When unset, <see cref="Content"/> is used.
    /// </summary>
    public object? Value
    {
        get => this.GetValue(ValueProperty);
        set => this.SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item can be selected.
    /// </summary>
    public bool IsEnabled
    {
        get => this.GetValue(IsEnabledProperty);
        set => this.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item is displayed.
    /// </summary>
    public bool IsVisible
    {
        get => this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }
}
