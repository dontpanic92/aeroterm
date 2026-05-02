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
