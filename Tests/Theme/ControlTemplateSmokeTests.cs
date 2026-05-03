// <copyright file="ControlTemplateSmokeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using ThemeNativeDropdown = AeroTerm.Theme.Controls.NativeDropdown;
using ThemeNativeDropdownItem = AeroTerm.Theme.Controls.NativeDropdownItem;

/// <summary>
/// Headless smoke tests for the AeroTerm control themes.
/// </summary>
[TestFixture]
public class ControlTemplateSmokeTests
{
    private static readonly (string Name, Func<Control> Factory, bool ExpectVisualChildren)[] ControlFactories =
    [
        ("Button", () => new Button { Content = "Button" }, true),
        ("PathIcon", () => new PathIcon { Data = Avalonia.Media.Geometry.Parse("M0 0L16 0L16 16L0 16Z") }, true),
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
        ("NativeDropdown", CreateNativeDropdown, true),
        ("NumericUpDown", () => new NumericUpDown { Minimum = 0, Maximum = 10, Value = 3 }, true),
        ("SplitButton", () => new SplitButton { Content = "Split" }, true),
        ("TabControl", CreateTabControl, true),
        ("GroupBox", () => new GroupBox { Header = "Group", Content = new TextBlock { Text = "Content" } }, true),
        ("Separator", () => new Separator { Width = 120 }, false),
        ("MenuItem", () => new MenuItem { Header = "Menu item" }, true),
        ("ContextMenu", CreateContextMenu, true),
    ];

    private static readonly (string Name, Func<ToggleButton> Factory)[] CheckableControlFactories =
    [
        ("CheckBox", () => new CheckBox { Content = "Check" }),
        ("RadioButton", () => new RadioButton { Content = "Radio" }),
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
    /// Gets checkable controls paired with the light and dark variants.
    /// </summary>
    public static IEnumerable<TestCaseData> CheckableControlCases
    {
        get
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                foreach (var (name, factory) in CheckableControlFactories)
                {
                    yield return new TestCaseData(variant, name, factory)
                        .SetName($"CheckableIndicatorClick_{variant}_{name}");
                }
            }
        }
    }

    /// <summary>
    /// Gets the theme variants covered by focused template assertions.
    /// </summary>
    public static IEnumerable<TestCaseData> ThemeVariants
    {
        get
        {
            yield return new TestCaseData(ThemeVariant.Light).SetName("TemplateFocus_Light");
            yield return new TestCaseData(ThemeVariant.Dark).SetName("TemplateFocus_Dark");
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

    /// <summary>
    /// Verifies the themed checkbox and radio indicator area participates in pointer hit testing.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    /// <param name="controlName">The display name of the control under test.</param>
    /// <param name="factory">Factory that creates the control under test.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(CheckableControlCases))]
    public void CheckableIndicatorClickTogglesControl(ThemeVariant variant, string controlName, Func<ToggleButton> factory)
    {
        var control = factory();
        var window = new Window
        {
            Width = 240,
            Height = 80,
            RequestedThemeVariant = variant,
            Content = control,
        };

        try
        {
            window.Show();
            PumpJobs();
            control.ApplyTemplate();
            PumpJobs();

            var indicatorCenter = control.TranslatePoint(
                new Point(8, control.Bounds.Height / 2),
                window) ?? throw new InvalidOperationException($"Could not translate {controlName} indicator coordinates.");

            window.MouseMove(indicatorCenter, RawInputModifiers.None);
            window.MouseDown(indicatorCenter, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(indicatorCenter, MouseButton.Left, RawInputModifiers.None);
            PumpJobs();

            Assert.That(control.IsChecked, Is.True, $"{controlName} should toggle when its indicator is clicked.");
        }
        finally
        {
            window.Close();
            PumpJobs();
        }
    }

    /// <summary>
    /// Verifies NumericUpDown focus is rendered by the outer spinner chrome only.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void NumericUpDownFocusUsesSingleOuterBorder(ThemeVariant variant)
    {
        var control = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 10,
            Value = 3,
        };
        var window = new Window
        {
            Width = 240,
            Height = 80,
            RequestedThemeVariant = variant,
            Content = control,
        };

        try
        {
            window.Show();
            PumpJobs();
            control.ApplyTemplate();
            PumpJobs();

            var spinner = FindTemplatePart<ButtonSpinner>(control, "PART_Spinner");
            var textBox = FindTemplatePart<TextBox>(control, "PART_TextBox");

            textBox.Focus(NavigationMethod.Tab, KeyModifiers.None);
            PumpJobs();

            var innerTextBoxBorder = textBox.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "LayoutRoot");

            Assert.That(spinner.BorderThickness, Is.EqualTo(new Thickness(2)));
            Assert.That(innerTextBoxBorder.BorderThickness, Is.EqualTo(new Thickness(0)));
        }
        finally
        {
            window.Close();
            PumpJobs();
        }
    }

    private static bool HasVisualChildren(Control control)
    {
        return control.GetVisualChildren().Any();
    }

    private static T FindTemplatePart<T>(Control control, string name)
        where T : Control
    {
        return control.GetVisualDescendants()
            .OfType<T>()
            .Single(part => part.Name == name);
    }

    private static ContextMenu CreateContextMenu()
    {
        return new ContextMenu
        {
            ItemsSource = new[] { new MenuItem { Header = "One" } },
        };
    }

    private static ThemeNativeDropdown CreateNativeDropdown()
    {
        var dropdown = new ThemeNativeDropdown();
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "One" });
        dropdown.Items.Add(new ThemeNativeDropdownItem { Content = "Two" });
        dropdown.SelectedIndex = 0;
        return dropdown;
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
