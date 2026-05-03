// <copyright file="NativeDropdown.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Metadata;

/// <summary>
/// Dropdown selector that uses native AppKit menus on macOS and Avalonia
/// menu fallbacks on Windows and Linux.
/// </summary>
public class NativeDropdown : Button
{
    /// <summary>
    /// Defines the <see cref="SelectedIndex"/> property.
    /// </summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<NativeDropdown, int>(
            nameof(SelectedIndex),
            defaultValue: -1,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="SelectedItem"/> property.
    /// </summary>
    public static readonly DirectProperty<NativeDropdown, NativeDropdownItem?> SelectedItemProperty =
        AvaloniaProperty.RegisterDirect<NativeDropdown, NativeDropdownItem?>(
            nameof(SelectedItem),
            o => o.SelectedItem);

    /// <summary>
    /// Defines the <see cref="SelectedValue"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> SelectedValueProperty =
        AvaloniaProperty.Register<NativeDropdown, object?>(
            nameof(SelectedValue),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="PlaceholderText"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<NativeDropdown, string?>(nameof(PlaceholderText), defaultValue: "Select");

    /// <summary>
    /// Defines the <see cref="ItemsSource"/> property.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<NativeDropdown, IEnumerable?>(nameof(ItemsSource));

    /// <summary>
    /// Defines the <see cref="DisplayMemberPath"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<NativeDropdown, string?>(nameof(DisplayMemberPath));

    private readonly NativeMenuFlyout menuFlyout;
    private bool preserveSelectedValue;
    private bool syncingSelection;
    private NativeDropdownItem? selectedItem;

    static NativeDropdown()
    {
        SelectedIndexProperty.Changed.AddClassHandler<NativeDropdown>((x, e) => x.OnSelectedIndexChanged(e));
        SelectedValueProperty.Changed.AddClassHandler<NativeDropdown>((x, e) => x.OnSelectedValueChanged(e));
        PlaceholderTextProperty.Changed.AddClassHandler<NativeDropdown>((x, _) => x.UpdateDisplayContent());
        ItemsSourceProperty.Changed.AddClassHandler<NativeDropdown>((x, _) => x.OnItemsSourceChanged());
        DisplayMemberPathProperty.Changed.AddClassHandler<NativeDropdown>((x, _) => x.OnItemsSourceChanged());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeDropdown"/> class.
    /// </summary>
    public NativeDropdown()
    {
        this.Items = new AvaloniaList<NativeDropdownItem>();
        this.Items.CollectionChanged += this.OnItemsCollectionChanged;
        this.menuFlyout = new NativeMenuFlyout();
        this.Flyout = this.menuFlyout;
        this.UpdateSelectionFromIndex(oldIndex: -1, raiseEvent: false);
    }

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event EventHandler<NativeDropdownSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Gets the dropdown items.
    /// </summary>
    [Content]
    public AvaloniaList<NativeDropdownItem> Items { get; }

    /// <summary>
    /// Gets or sets the selected index, or <c>-1</c> when nothing is selected.
    /// </summary>
    public int SelectedIndex
    {
        get => this.GetValue(SelectedIndexProperty);
        set => this.SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets the selected item, or <c>null</c> when nothing is selected.
    /// </summary>
    public NativeDropdownItem? SelectedItem
    {
        get => this.selectedItem;
        private set => this.SetAndRaise(SelectedItemProperty, ref this.selectedItem, value);
    }

    /// <summary>
    /// Gets or sets the selected item value, or <c>null</c> when nothing is selected.
    /// </summary>
    public object? SelectedValue
    {
        get => this.GetValue(SelectedValueProperty);
        set => this.SetValue(SelectedValueProperty, value);
    }

    /// <summary>
    /// Gets or sets text displayed when no item is selected.
    /// </summary>
    public string? PlaceholderText
    {
        get => this.GetValue(PlaceholderTextProperty);
        set => this.SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the data source used to generate dropdown items.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => this.GetValue(ItemsSourceProperty);
        set => this.SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the source item property displayed in the dropdown.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => this.GetValue(DisplayMemberPathProperty);
        set => this.SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Opens the dropdown menu.
    /// </summary>
    public void OpenDropdown()
    {
        this.RebuildMenu();
        this.menuFlyout.ShowAt(this);
    }

    /// <summary>
    /// Rebuilds the menu items from the current dropdown items.
    /// </summary>
    internal void RebuildMenu()
    {
        this.menuFlyout.Items.Clear();
        IReadOnlyList<NativeDropdownItem> items = this.GetEffectiveItems();
        for (int i = 0; i < items.Count; i++)
        {
            NativeDropdownItem item = items[i];
            if (!item.IsVisible)
            {
                continue;
            }

            int capturedIndex = i;
            var menuItem = new NativeMenuItem
            {
                Header = item.Content,
                IsEnabled = item.IsEnabled,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = capturedIndex == this.SelectedIndex,
            };
            menuItem.Click += (_, _) => this.SelectedIndex = capturedIndex;
            this.menuFlyout.Items.Add(menuItem);
        }
    }

    /// <summary>
    /// Gets the menu flyout used to display dropdown items.
    /// </summary>
    /// <returns>The menu flyout.</returns>
    internal NativeMenuFlyout GetMenuFlyout()
    {
        return this.menuFlyout;
    }

    /// <inheritdoc/>
    protected override void OnClick()
    {
        this.RebuildMenu();
        base.OnClick();
    }

    private void OnSelectedIndexChanged(AvaloniaPropertyChangedEventArgs e)
    {
        int oldIndex = e.OldValue is int old ? old : -1;
        IReadOnlyList<NativeDropdownItem> items = this.GetEffectiveItems();
        if (this.SelectedIndex < -1 || this.SelectedIndex >= items.Count)
        {
            this.SelectedIndex = -1;
            return;
        }

        this.UpdateSelectionFromIndex(oldIndex, raiseEvent: true);
    }

    private void OnSelectedValueChanged(AvaloniaPropertyChangedEventArgs e)
    {
        _ = e;
        if (this.syncingSelection)
        {
            return;
        }

        int index = this.FindIndexByValue(this.SelectedValue);
        if (index >= 0)
        {
            this.SelectedIndex = index;
            this.UpdateSelectionFromIndex(oldIndex: -1, raiseEvent: false);
            return;
        }

        this.preserveSelectedValue = true;
        try
        {
            this.SelectedIndex = -1;
            this.SelectedItem = null;
            this.UpdateDisplayContent();
        }
        finally
        {
            this.preserveSelectedValue = false;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        this.ReconcileSelectionAfterItemsChanged();
    }

    private void OnItemsSourceChanged()
    {
        this.ReconcileSelectionAfterItemsChanged();
    }

    private void ReconcileSelectionAfterItemsChanged()
    {
        IReadOnlyList<NativeDropdownItem> items = this.GetEffectiveItems();
        int valueIndex = this.FindIndexByValue(this.SelectedValue);
        if (valueIndex >= 0 && valueIndex != this.SelectedIndex)
        {
            this.SelectedIndex = valueIndex;
            return;
        }

        if (this.SelectedIndex >= items.Count)
        {
            this.SelectedIndex = -1;
            return;
        }

        this.UpdateSelectionFromIndex(this.SelectedIndex, raiseEvent: false);
    }

    private void UpdateSelectionFromIndex(int oldIndex, bool raiseEvent)
    {
        IReadOnlyList<NativeDropdownItem> items = this.GetEffectiveItems();
        NativeDropdownItem? oldItem = oldIndex >= 0 && oldIndex < items.Count
            ? items[oldIndex]
            : this.SelectedItem;

        NativeDropdownItem? newItem = this.SelectedIndex >= 0 && this.SelectedIndex < items.Count
            ? items[this.SelectedIndex]
            : null;

        this.SelectedItem = newItem;
        if (!this.preserveSelectedValue)
        {
            this.syncingSelection = true;
            try
            {
                this.SelectedValue = newItem?.Value ?? newItem?.Content;
            }
            finally
            {
                this.syncingSelection = false;
            }
        }

        this.UpdateDisplayContent();

        if (raiseEvent && oldItem != newItem)
        {
            this.SelectionChanged?.Invoke(
                this,
                new NativeDropdownSelectionChangedEventArgs(oldIndex, oldItem, this.SelectedIndex, newItem));
        }
    }

    private void UpdateDisplayContent()
    {
        this.Content = this.SelectedItem?.Content ?? this.PlaceholderText;
    }

    private IReadOnlyList<NativeDropdownItem> GetEffectiveItems()
    {
        if (this.ItemsSource is null)
        {
            return this.Items;
        }

        return this.ItemsSource
            .Cast<object?>()
            .Select(this.CreateItemFromSource)
            .ToArray();
    }

    private NativeDropdownItem CreateItemFromSource(object? source)
    {
        return new NativeDropdownItem
        {
            Content = this.GetDisplayValue(source),
            Value = source,
        };
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "DisplayMemberPath is a caller-provided UI path; AOT apps must statically root displayed members.")]
    private object? GetDisplayValue(object? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(this.DisplayMemberPath))
        {
            return source;
        }

        PropertyInfo? property = source.GetType().GetProperty(this.DisplayMemberPath);
        return property is null ? source : property.GetValue(source);
    }

    private int FindIndexByValue(object? value)
    {
        IReadOnlyList<NativeDropdownItem> items = this.GetEffectiveItems();
        for (int i = 0; i < items.Count; i++)
        {
            object? itemValue = items[i].Value ?? items[i].Content;
            if (Equals(itemValue, value))
            {
                return i;
            }
        }

        return -1;
    }
}
