// <copyright file="TabStripGroupHeadlessTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.UI;

using System;
using System.IO;
using System.Linq;
using AeroTerm.Controls;
using AeroTerm.Services;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using NUnit.Framework;
using IOPath = System.IO.Path;
using ThemeNativeContextMenu = AeroTerm.Theme.Controls.NativeContextMenu;
using ThemeNativeMenuItem = AeroTerm.Theme.Controls.NativeMenuItem;

/// <summary>
/// Headless UI tests for the tab-group visuals: the colored pill above
/// a grouped tab header, the "Add to group" context-menu submenu, and
/// the "Remove from group" entry's enabled state.
/// </summary>
[TestFixture]
public class TabStripGroupHeadlessTests
{
    /// <summary>
    /// Assigning a <see cref="TabSession.GroupId"/> to a known group
    /// flips the header's pill rectangle to visible and paints it the
    /// group color.
    /// </summary>
    [AvaloniaTest]
    public void GroupPill_VisibleWithGroupColor_WhenTabAssigned()
    {
        var tempDir = IOPath.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "grouppill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new TabGroupStore(tempDir);
            var view = new TabView();
            var strip = new TabStrip { View = view, GroupStore = store };

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(strip, Dock.Top);
            root.Children.Add(strip);
            root.Children.Add(view);

            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = root,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var session = new TabSession(new FakeTabContent("one"));
                view.AddTab(session);
                Dispatcher.UIThread.RunJobs();

                var group = store.CreateGroup("Work");
                session.GroupId = group.Id;
                Dispatcher.UIThread.RunJobs();

                var pill = strip.GetLogicalDescendants()
                    .OfType<Rectangle>()
                    .FirstOrDefault(r => r.IsVisible && r.Height == 3);
                Assert.That(pill, Is.Not.Null, "expected a 3px-tall visible pill rectangle on the header");

                var fill = pill!.Fill as ISolidColorBrush;
                Assert.That(fill, Is.Not.Null);
                var expected = Color.FromRgb(
                    (byte)((group.Color >> 16) & 0xFF),
                    (byte)((group.Color >> 8) & 0xFF),
                    (byte)(group.Color & 0xFF));
                Assert.That(fill!.Color, Is.EqualTo(expected));

                session.GroupId = null;
                Dispatcher.UIThread.RunJobs();
                var hiddenPill = strip.GetLogicalDescendants()
                    .OfType<Rectangle>()
                    .FirstOrDefault(r => r.Height == 3);
                Assert.That(hiddenPill, Is.Not.Null);
                Assert.That(hiddenPill!.IsVisible, Is.False);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// The per-header context menu includes an "Add to group" submenu
    /// and a "Remove from group" entry. The remove entry is disabled
    /// when the tab has no group assignment and enabled once a group is
    /// assigned.
    /// </summary>
    [AvaloniaTest]
    public void ContextMenu_HasGroupEntries_ReflectsAssignment()
    {
        var tempDir = IOPath.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "groupmenu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new TabGroupStore(tempDir);
            var group = store.CreateGroup("Focus");

            var view = new TabView();
            var strip = new TabStrip { View = view, GroupStore = store };

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(strip, Dock.Top);
            root.Children.Add(strip);
            root.Children.Add(view);

            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = root,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var session = new TabSession(new FakeTabContent("one"));
                view.AddTab(session);
                Dispatcher.UIThread.RunJobs();

                var header = strip.GetLogicalDescendants()
                    .OfType<Border>()
                    .First(b => b.GetLogicalParent() is StackPanel sp
                        && sp.Orientation == Avalonia.Layout.Orientation.Horizontal);
                var menu = ThemeNativeContextMenu.GetMenu(header);
                Assert.That(menu, Is.Not.Null);
                var items = menu!.Items.OfType<ThemeNativeMenuItem>().ToList();
                var headers = items.Select(m => m.Header?.ToString()).ToList();
                Assert.That(headers, Does.Contain("Add to group"));
                Assert.That(headers, Does.Contain("Remove from group"));

                var remove = items.First(m => m.Header?.ToString() == "Remove from group");
                Assert.That(remove.IsEnabled, Is.False, "remove should be disabled when ungrouped");

                var addSubmenu = items.First(m => m.Header?.ToString() == "Add to group");
                var subHeaders = addSubmenu.Items
                    .OfType<ThemeNativeMenuItem>()
                    .Select(m => m.Header?.ToString())
                    .ToList();
                Assert.That(subHeaders, Does.Contain("Focus"));
                Assert.That(subHeaders, Does.Contain("New group…"));

                session.GroupId = group.Id;
                Dispatcher.UIThread.RunJobs();

                // Context menu is rebuilt on each group refresh.
                var header2 = strip.GetLogicalDescendants()
                    .OfType<Border>()
                    .First(b => b.GetLogicalParent() is StackPanel sp
                        && sp.Orientation == Avalonia.Layout.Orientation.Horizontal);
                var menu2 = ThemeNativeContextMenu.GetMenu(header2);
                Assert.That(menu2, Is.Not.Null);
                var items2 = menu2!.Items.OfType<ThemeNativeMenuItem>().ToList();
                var remove2 = items2.First(m => m.Header?.ToString() == "Remove from group");
                Assert.That(remove2.IsEnabled, Is.True, "remove should be enabled after assignment");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
