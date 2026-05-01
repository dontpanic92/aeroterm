// <copyright file="ControlTemplateSmokeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;

/// <summary>
/// Headless smoke tests for the AeroTerm control themes.
/// </summary>
[TestFixture]
public class ControlTemplateSmokeTests
{
    private static readonly (string Name, Func<Control> Factory, bool ExpectVisualChildren)[] ControlFactories =
    [
        ("Button", () => new Button { Content = "Button" }, true),
        ("ToggleButton", () => new ToggleButton { Content = "Toggle" }, true),
        ("CheckBox", () => new CheckBox { Content = "Check" }, true),
        ("RadioButton", () => new RadioButton { Content = "Radio" }, true),
        ("TextBox", () => new TextBox { Text = "Text" }, true),
        ("ListBox", () => new ListBox { ItemsSource = new[] { "One", "Two" } }, true),
        ("ScrollViewer", () => new ScrollViewer { Content = new TextBlock { Text = "Scrollable" } }, true),
        ("ScrollBar", () => new ScrollBar { Minimum = 0, Maximum = 100, ViewportSize = 10 }, true),
        ("Slider", () => new Slider { Minimum = 0, Maximum = 100, Value = 50 }, true),
        ("ProgressBar", () => new ProgressBar { Minimum = 0, Maximum = 100, Value = 50 }, true),
        ("ComboBox", () => new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 }, true),
        ("NumericUpDown", () => new NumericUpDown { Minimum = 0, Maximum = 10, Value = 3 }, true),
        ("SplitButton", () => new SplitButton { Content = "Split" }, true),
        ("TabControl", CreateTabControl, true),
        ("GroupBox", () => new GroupBox { Header = "Group", Content = new TextBlock { Text = "Content" } }, true),
        ("Separator", () => new Separator { Width = 120 }, false),
        ("MenuItem", () => new MenuItem { Header = "Menu item" }, true),
        ("ContextMenu", CreateContextMenu, true),
    ];

    /// <summary>
    /// Gets themed controls paired with the light and dark variants.
    /// </summary>
    public static IEnumerable<TestCaseData> ControlCases
    {
        get
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                foreach (var (name, factory, expectVisualChildren) in ControlFactories)
                {
                    yield return new TestCaseData(variant, name, factory, expectVisualChildren)
                        .SetName($"ControlTemplate_{variant}_{name}");
                }
            }
        }
    }

    /// <summary>
    /// Verifies a themed control can attach to a headless visual tree and apply its template.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    /// <param name="controlName">The display name of the control under test.</param>
    /// <param name="factory">Factory that creates the control under test.</param>
    /// <param name="expectVisualChildren">Whether template application is expected to create visual children.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ControlCases))]
    public void ControlTemplateApplies(ThemeVariant variant, string controlName, Func<Control> factory, bool expectVisualChildren)
    {
        var control = factory();
        var window = new Window
        {
            Width = 480,
            Height = 320,
            RequestedThemeVariant = variant,
        };

        try
        {
            if (control is ContextMenu contextMenu)
            {
                var target = new Button { Content = "Target", ContextMenu = contextMenu };
                window.Content = new VisualLayerManager
                {
                    EnableOverlayLayer = true,
                    Child = target,
                };
                window.Show();
                window.ApplyTemplate();
                PumpJobs();

                contextMenu.Open(target);
                PumpJobs();
                contextMenu.ApplyTemplate();
                PumpJobs();

                Assert.That(contextMenu.IsOpen, Is.True, $"{controlName} should open before template assertions.");
            }
            else if (control is MenuItem menuItem)
            {
                var menu = new Menu();
                menu.Items.Add(menuItem);
                window.Content = menu;
                window.Show();
                PumpJobs();

                menuItem.ApplyTemplate();
                PumpJobs();
            }
            else
            {
                window.Content = control;
                window.Show();
                PumpJobs();

                control.ApplyTemplate();
                PumpJobs();
            }

            if (expectVisualChildren)
            {
                Assert.That(HasVisualChildren(control), Is.True, $"{controlName} should produce visual children.");
            }
        }
        finally
        {
            if (control is ContextMenu contextMenu)
            {
                contextMenu.Close();
            }

            window.Close();
            PumpJobs();
        }
    }

    private static bool HasVisualChildren(Control control)
    {
        return control.GetVisualChildren().Any();
    }

    private static ContextMenu CreateContextMenu()
    {
        return new ContextMenu
        {
            ItemsSource = new[] { new MenuItem { Header = "One" } },
        };
    }

    private static TabControl CreateTabControl()
    {
        return new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem
                {
                    Header = "One",
                    Content = new TextBlock { Text = "Page" },
                },
            },
        };
    }

    private static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
