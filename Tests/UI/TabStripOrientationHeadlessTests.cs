// <copyright file="TabStripOrientationHeadlessTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.UI;

using System.Linq;
using AeroTerm.Controls;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NUnit.Framework;

/// <summary>
/// Headless UI tests for the session-28 vertical tab rail. Exercises
/// <see cref="TabStrip.Orientation"/> and asserts observable layout
/// differences between the horizontal and vertical modes.
/// </summary>
[TestFixture]
public class TabStripOrientationHeadlessTests
{
    /// <summary>
    /// Default orientation is horizontal: the internal tabs panel uses
    /// <see cref="Orientation.Horizontal"/>.
    /// </summary>
    [AvaloniaTest]
    public void TabStrip_DefaultsToHorizontal()
    {
        var (window, strip, _) = BuildHostedStrip();
        try
        {
            Assert.That(strip.Orientation, Is.EqualTo(Orientation.Horizontal));
            var tabsPanel = FindTabsPanel(strip);
            Assert.That(tabsPanel, Is.Not.Null);
            Assert.That(tabsPanel!.Orientation, Is.EqualTo(Orientation.Horizontal));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Flipping orientation to vertical changes the internal tabs panel
    /// orientation and yields distinctly different header bounds (tabs
    /// stack top-to-bottom rather than left-to-right).
    /// </summary>
    [AvaloniaTest]
    public void TabStrip_VerticalOrientation_ChangesLayout()
    {
        var (window, strip, view) = BuildHostedStrip();
        try
        {
            view.AddTab(new TabSession(new FakeTabContent("a")));
            view.AddTab(new TabSession(new FakeTabContent("b")));
            view.AddTab(new TabSession(new FakeTabContent("c")));
            Dispatcher.UIThread.RunJobs();

            var headersH = FindTabHeaderBorders(strip).ToList();
            Assert.That(headersH.Count, Is.EqualTo(3));
            double h0x = headersH[0].Bounds.X;
            double h1x = headersH[1].Bounds.X;
            Assert.That(h1x, Is.GreaterThan(h0x), "Horizontal mode should lay headers out along X.");

            strip.Orientation = Orientation.Vertical;
            Dispatcher.UIThread.RunJobs();

            var tabsPanel = FindTabsPanel(strip);
            Assert.That(tabsPanel, Is.Not.Null);
            Assert.That(tabsPanel!.Orientation, Is.EqualTo(Orientation.Vertical));

            var headersV = FindTabHeaderBorders(strip).ToList();
            Assert.That(headersV.Count, Is.EqualTo(3));
            double v0y = headersV[0].Bounds.Y;
            double v1y = headersV[1].Bounds.Y;
            Assert.That(v1y, Is.GreaterThan(v0y), "Vertical mode should lay headers out along Y.");

            // Flipping back restores horizontal layout.
            strip.Orientation = Orientation.Horizontal;
            Dispatcher.UIThread.RunJobs();
            Assert.That(FindTabsPanel(strip)!.Orientation, Is.EqualTo(Orientation.Horizontal));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Orientation changes are idempotent / stable across tab collection
    /// changes — adding a new tab after the flip lays it out vertically.
    /// </summary>
    [AvaloniaTest]
    public void TabStrip_Vertical_NewTabLaysOutVertically()
    {
        var (window, strip, view) = BuildHostedStrip();
        try
        {
            view.AddTab(new TabSession(new FakeTabContent("a")));
            Dispatcher.UIThread.RunJobs();
            strip.Orientation = Orientation.Vertical;
            Dispatcher.UIThread.RunJobs();

            view.AddTab(new TabSession(new FakeTabContent("b")));
            Dispatcher.UIThread.RunJobs();

            var headers = FindTabHeaderBorders(strip).ToList();
            Assert.That(headers.Count, Is.EqualTo(2));
            Assert.That(headers[1].Bounds.Y, Is.GreaterThan(headers[0].Bounds.Y));
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, TabStrip Strip, TabView View) BuildHostedStrip()
    {
        var view = new TabView();
        var strip = new TabStrip { View = view };

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
        return (window, strip, view);
    }

    private static StackPanel? FindTabsPanel(TabStrip strip)
    {
        // The tab strip hosts exactly one StackPanel that holds the tab
        // headers (plus a sibling SplitButton for "+"); filter by parent
        // type (DockPanel) to pick it unambiguously.
        return strip.GetLogicalDescendants()
            .OfType<StackPanel>()
            .FirstOrDefault(sp => sp.GetLogicalParent() is DockPanel);
    }

    private static System.Collections.Generic.IEnumerable<Border> FindTabHeaderBorders(TabStrip strip)
    {
        // Tab headers are Borders whose logical parent is the tabs StackPanel.
        // We identify the panel by its DockPanel parent so this helper works
        // in either orientation.
        var panel = FindTabsPanel(strip);
        if (panel is null)
        {
            yield break;
        }

        foreach (var child in panel.Children)
        {
            if (child is Border b && child is not Avalonia.Controls.Shapes.Shape)
            {
                yield return b;
            }
        }
    }
}
