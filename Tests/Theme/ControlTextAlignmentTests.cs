// <copyright file="ControlTextAlignmentTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using ThemeNativeDropdown = AeroTerm.Theme.Controls.NativeDropdown;
using ThemeNativeDropdownItem = AeroTerm.Theme.Controls.NativeDropdownItem;

/// <summary>
/// Headless layout checks for AeroTerm control text alignment.
/// </summary>
[TestFixture]
public class ControlTextAlignmentTests
{
    /// <summary>
    /// Gets the light and dark theme variants covered by alignment tests.
    /// </summary>
    public static IEnumerable<TestCaseData> ThemeVariants
    {
        get
        {
            yield return new TestCaseData(ThemeVariant.Light).SetName("ControlTextAlignment_Light");
            yield return new TestCaseData(ThemeVariant.Dark).SetName("ControlTextAlignment_Dark");
        }
    }

    /// <summary>
    /// Verifies button-like controls use the shared asymmetric content padding that drops text to the visual baseline.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void ButtonLikeControlsUseBaselinePadding(ThemeVariant variant)
    {
        var controls = new TemplatedControl[]
        {
            new Button { Content = "Button" },
            new RepeatButton { Content = "Repeat" },
            new ToggleButton { Content = "Toggle" },
            new HyperlinkButton { Content = "Link" },
        };

        foreach (var control in controls)
        {
            var host = ShowControl(variant, control);
            try
            {
                var presenter = FindTemplatePart<ContentPresenter>(control, "PART_ContentPresenter");

                Assert.That(presenter.Padding.Top, Is.GreaterThan(presenter.Padding.Bottom), control.GetType().Name);
                Assert.That(presenter.Padding.Left, Is.EqualTo(presenter.Padding.Right), control.GetType().Name);
                Assert.That(presenter.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center), control.GetType().Name);
            }
            finally
            {
                CloseHost(host);
            }
        }
    }

    /// <summary>
    /// Verifies checkable labels receive the shared text baseline correction independently of their glyph margin.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void CheckableControlsUseBaselineLabelPadding(ThemeVariant variant)
    {
        var controls = new TemplatedControl[]
        {
            new CheckBox { Content = "Check" },
            new RadioButton { Content = "Radio" },
        };

        foreach (var control in controls)
        {
            var host = ShowControl(variant, control);
            try
            {
                var presenter = FindTemplatePart<ContentPresenter>(control, "PART_ContentPresenter");

                Assert.That(presenter.Margin.Left, Is.GreaterThan(0), control.GetType().Name);
                Assert.That(presenter.Padding.Top, Is.GreaterThan(0), control.GetType().Name);
                Assert.That(presenter.Padding.Bottom, Is.EqualTo(0), control.GetType().Name);
                Assert.That(presenter.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center), control.GetType().Name);
            }
            finally
            {
                CloseHost(host);
            }
        }
    }

    /// <summary>
    /// Verifies text-entry controls align real and placeholder text through the text-hosting presenter.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void TextBoxAlignsTextPresenterAndWatermark(ThemeVariant variant)
    {
        var textBox = new TextBox
        {
            Text = "Text",
            PlaceholderText = "Placeholder",
        };

        var host = ShowControl(variant, textBox);
        try
        {
            var presenter = FindTemplatePart<TextPresenter>(textBox, "PART_TextPresenter");
            var watermark = FindTemplatePart<TextBlock>(textBox, "PART_Watermark");

            Assert.That(presenter.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
            Assert.That(watermark.VerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
            Assert.That(presenter.Parent, Is.TypeOf<Grid>());
            Assert.That(watermark.Parent, Is.SameAs(presenter.Parent));
        }
        finally
        {
            CloseHost(host);
        }
    }

    /// <summary>
    /// Verifies picker controls inherit the shared tight baseline padding.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void PickerControlsUseTightBaselinePadding(ThemeVariant variant)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "One", "Two" },
            SelectedIndex = 0,
        };
        var nativeDropdown = new ThemeNativeDropdown();
        nativeDropdown.Items.Add(new ThemeNativeDropdownItem { Content = "One" });
        nativeDropdown.Items.Add(new ThemeNativeDropdownItem { Content = "Two" });
        nativeDropdown.SelectedIndex = 0;

        var comboHost = ShowControl(variant, comboBox);
        var nativeHost = ShowControl(variant, nativeDropdown);
        try
        {
            var comboPresenter = FindTemplatePart<ContentPresenter>(comboBox, "PART_ContentPresenter");
            var nativePresenter = FindTemplatePart<ContentPresenter>(nativeDropdown, "PART_ContentPresenter");

            Assert.That(comboPresenter.Margin.Top, Is.GreaterThan(comboPresenter.Margin.Bottom));
            Assert.That(nativePresenter.Margin.Top, Is.GreaterThan(nativePresenter.Margin.Bottom));
        }
        finally
        {
            CloseHost(nativeHost);
            CloseHost(comboHost);
        }
    }

    /// <summary>
    /// Verifies menu header text no longer uses a hard-coded local offset.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void MenuItemHeaderUsesSharedBaselinePadding(ThemeVariant variant)
    {
        var menuItem = new MenuItem { Header = "Menu item" };
        var menu = new Menu();
        menu.Items.Add(menuItem);

        var host = ShowControl(variant, menu);
        try
        {
            menuItem.ApplyTemplate();
            PumpJobs();

            var presenter = FindTemplatePart<ContentPresenter>(menuItem, "PART_HeaderPresenter");
            Assert.That(presenter.Padding.Top, Is.GreaterThan(0));
            Assert.That(presenter.Padding.Bottom, Is.EqualTo(0));
        }
        finally
        {
            CloseHost(host);
        }
    }

    /// <summary>
    /// Verifies baseline correction tokens resolve to asymmetric values.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void BaselinePaddingTokensResolve(ThemeVariant variant)
    {
        var window = new Window
        {
            RequestedThemeVariant = variant,
        };

        try
        {
            window.Show();
            AssertThicknessResource(window, variant, "ControlPaddingTight", topGreaterThanBottom: true);
            AssertThicknessResource(window, variant, "ControlPaddingDefault", topGreaterThanBottom: true);
            AssertThicknessResource(window, variant, "ControlPaddingRoomy", topGreaterThanBottom: true);
            AssertThicknessResource(window, variant, "ControlTextBaselinePadding", topGreaterThanBottom: true);
        }
        finally
        {
            window.Close();
            PumpJobs();
        }
    }

    private static Window ShowControl(ThemeVariant variant, Control control)
    {
        var window = new Window
        {
            Width = 360,
            Height = 120,
            RequestedThemeVariant = variant,
            Content = control,
        };

        window.Show();
        PumpJobs();
        control.ApplyTemplate();
        PumpJobs();
        return window;
    }

    private static void CloseHost(Window window)
    {
        window.Close();
        PumpJobs();
    }

    private static T FindTemplatePart<T>(Control control, string name)
        where T : Control
    {
        return control.GetVisualDescendants()
            .OfType<T>()
            .Single(part => part.Name == name);
    }

    private static void AssertThicknessResource(Window window, ThemeVariant variant, string key, bool topGreaterThanBottom)
    {
        Assert.That(window.TryFindResource(key, variant, out var value), Is.True, $"Resource '{key}' should resolve.");
        Assert.That(value, Is.TypeOf<Thickness>(), $"Resource '{key}' should be a Thickness.");

        var thickness = (Thickness)value!;
        if (topGreaterThanBottom)
        {
            Assert.That(thickness.Top, Is.GreaterThan(thickness.Bottom), key);
        }
    }

    private static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
