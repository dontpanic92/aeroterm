// <copyright file="NativeMenuWrapperTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Linq;
using System.Windows.Input;
using AeroTerm.Theme.NativeMenus;
using Avalonia.Controls;
using NUnit.Framework;
using AvaloniaMenuItem = Avalonia.Controls.MenuItem;
using ThemeNativeContextMenu = AeroTerm.Theme.Controls.NativeContextMenu;
using ThemeNativeDropdown = AeroTerm.Theme.Controls.NativeDropdown;
using ThemeNativeDropdownItem = AeroTerm.Theme.Controls.NativeDropdownItem;
using ThemeNativeMenuItem = AeroTerm.Theme.Controls.NativeMenuItem;
using ThemeNativeMenuSeparator = AeroTerm.Theme.Controls.NativeMenuSeparator;

/// <summary>
/// Tests for the native menu wrapper model and Avalonia fallback adapter.
/// </summary>
[TestFixture]
public class NativeMenuWrapperTests
{
    /// <summary>
    /// Invoking a menu item executes its command and raises Click.
    /// </summary>
    [Test]
    public void Invoke_EnabledItem_ExecutesCommandAndRaisesClick()
    {
        var command = new RecordingCommand(canExecute: true);
        var item = new ThemeNativeMenuItem
        {
            Command = command,
            CommandParameter = "payload",
        };
        bool clicked = false;
        item.Click += (_, _) => clicked = true;

        item.Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(command.ExecuteCount, Is.EqualTo(1));
            Assert.That(command.LastParameter, Is.EqualTo("payload"));
            Assert.That(clicked, Is.True);
        });
    }

    /// <summary>
    /// Disabled commands are not executed and do not raise Click.
    /// </summary>
    [Test]
    public void Invoke_CommandCannotExecute_DoesNotRaiseClick()
    {
        var command = new RecordingCommand(canExecute: false);
        var item = new ThemeNativeMenuItem
        {
            Command = command,
        };
        bool clicked = false;
        item.Click += (_, _) => clicked = true;

        item.Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(command.ExecuteCount, Is.Zero);
            Assert.That(clicked, Is.False);
        });
    }

    /// <summary>
    /// The Avalonia fallback preserves headers, separators, and submenus.
    /// </summary>
    [Test]
    public void AvaloniaFallback_ConvertsNestedMenuItems()
    {
        var root = new ThemeNativeMenuItem { Header = "Root" };
        root.Items.Add(new ThemeNativeMenuItem { Header = "Child" });
        root.Items.Add(new ThemeNativeMenuSeparator());

        var converted = AvaloniaNativeMenuAdapter.CreateAvaloniaItem(root);

        Assert.That(converted, Is.TypeOf<AvaloniaMenuItem>());
        var menuItem = (AvaloniaMenuItem)converted!;
        Assert.Multiple(() =>
        {
            Assert.That(menuItem.Header, Is.EqualTo("Root"));
            Assert.That(menuItem.Items.OfType<AvaloniaMenuItem>().Single().Header, Is.EqualTo("Child"));
            Assert.That(menuItem.Items.OfType<Separator>().Count(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Native context menus can be attached to ordinary Avalonia controls.
    /// </summary>
    [Test]
    public void NativeContextMenu_AttachedProperty_RoundTrips()
    {
        var target = new Border();
        var menu = new ThemeNativeContextMenu();

        ThemeNativeContextMenu.SetMenu(target, menu);

        Assert.That(ThemeNativeContextMenu.GetMenu(target), Is.SameAs(menu));
    }

    /// <summary>
    /// Selecting a dropdown item updates selected item, selected value, and displayed content.
    /// </summary>
    [Test]
    public void NativeDropdown_SelectedIndex_UpdatesSelectionState()
    {
        var dropdown = new ThemeNativeDropdown();
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "One", Value = 1 });
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "Two", Value = 2 });
        NativeDropdownSelectionSnapshot? snapshot = null;
        dropdown.SelectionChanged += (_, e) =>
        {
            snapshot = new NativeDropdownSelectionSnapshot(e.OldIndex, e.NewIndex, e.NewItem?.Content);
        };

        dropdown.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(dropdown.SelectedItem, Is.SameAs(dropdown.Items[1]));
            Assert.That(dropdown.SelectedValue, Is.EqualTo(2));
            Assert.That(dropdown.Content, Is.EqualTo("Two"));
            Assert.That(snapshot?.OldIndex, Is.EqualTo(-1));
            Assert.That(snapshot?.NewIndex, Is.EqualTo(1));
            Assert.That(snapshot?.NewContent, Is.EqualTo("Two"));
        });
    }

    /// <summary>
    /// Dropdown menus are generated as checked native menu items that select their source item.
    /// </summary>
    [Test]
    public void NativeDropdown_RebuildMenu_CreatesSelectableNativeMenuItems()
    {
        var dropdown = new ThemeNativeDropdown();
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "One" });
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "Two", IsEnabled = false });
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "Hidden", IsVisible = false });
        dropdown.SelectedIndex = 0;

        dropdown.RebuildMenu();

        var items = dropdown.GetMenuFlyout().Items.OfType<ThemeNativeMenuItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.Header), Is.EqualTo(new[] { "One", "Two" }));
            Assert.That(items[0].IsChecked, Is.True);
            Assert.That(items[1].IsEnabled, Is.False);
        });

        items[1].Invoke();
        Assert.That(dropdown.SelectedIndex, Is.EqualTo(0));

        items[0].Invoke();
        Assert.That(dropdown.SelectedIndex, Is.EqualTo(0));
    }

    /// <summary>
    /// Item-source dropdowns preserve an externally supplied selected value
    /// until the matching source items arrive.
    /// </summary>
    [Test]
    public void NativeDropdown_ItemsSource_MatchesPreselectedValue()
    {
        var dropdown = new ThemeNativeDropdown
        {
            SelectedValue = "Two",
        };

        dropdown.ItemsSource = new[] { "One", "Two" };

        Assert.Multiple(() =>
        {
            Assert.That(dropdown.SelectedIndex, Is.EqualTo(1));
            Assert.That(dropdown.SelectedValue, Is.EqualTo("Two"));
            Assert.That(dropdown.Content, Is.EqualTo("Two"));
        });
    }

    /// <summary>
    /// Item-source dropdowns can display a property while selecting the
    /// original source object as the value.
    /// </summary>
    [Test]
    public void NativeDropdown_ItemsSource_UsesDisplayMemberPath()
    {
        var first = new DisplayItem("One");
        var second = new DisplayItem("Two");
        var dropdown = new ThemeNativeDropdown
        {
            ItemsSource = new[] { first, second },
            DisplayMemberPath = nameof(DisplayItem.Name),
            SelectedValue = second,
        };

        dropdown.RebuildMenu();

        var items = dropdown.GetMenuFlyout().Items.OfType<ThemeNativeMenuItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(dropdown.SelectedIndex, Is.EqualTo(1));
            Assert.That(dropdown.Content, Is.EqualTo("Two"));
            Assert.That(items.Select(i => i.Header), Is.EqualTo(new[] { "One", "Two" }));
            Assert.That(items[1].IsChecked, Is.True);
        });
    }

    private sealed record NativeDropdownSelectionSnapshot(int OldIndex, int NewIndex, object? NewContent);

    private sealed record DisplayItem(string Name);

    private sealed class RecordingCommand : ICommand
    {
        private readonly bool canExecute;

        public RecordingCommand(bool canExecute)
        {
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter)
        {
            _ = parameter;
            return this.canExecute;
        }

        public void Execute(object? parameter)
        {
            this.ExecuteCount++;
            this.LastParameter = parameter;
        }
    }
}
