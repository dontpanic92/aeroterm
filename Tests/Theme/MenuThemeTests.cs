// <copyright file="MenuThemeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System.Collections.Generic;
using System.Linq;
using AeroTerm.Theme.Helpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;

/// <summary>
/// Focused assertions for the Windows/Linux Avalonia-backed menu theme.
/// </summary>
[TestFixture]
public class MenuThemeTests
{
    /// <summary>
    /// Gets the theme variants covered by menu template assertions.
    /// </summary>
    public static IEnumerable<TestCaseData> ThemeVariants
    {
        get
        {
            yield return new TestCaseData(ThemeVariant.Light).SetName("MenuTheme_Light");
            yield return new TestCaseData(ThemeVariant.Dark).SetName("MenuTheme_Dark");
        }
    }

    /// <summary>
    /// Verifies the light menu tokens match the screenshot-derived reference colors.
    /// </summary>
    [AvaloniaTest]
    [Test]
    public void LightMenuTokensMatchScreenshotReferences()
    {
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Light,
        };

        try
        {
            window.Show();
            PumpJobs();

            AssertBrushColor(window, "MenuPopupBackgroundBrush", "#FFFFFEFC");
            AssertBrushColor(window, "MenuItemHoverBrush", "#FFF0EFED");
            AssertBrushColor(window, "MenuSeparatorBrush", "#FFEBEAE8");
            AssertBrushColor(window, "MenuItemForegroundBrush", "#FF222222");
            AssertBrushColor(window, "MenuAccentGlyphBrush", "#FF0C74C3");
        }
        finally
        {
            window.Close();
            PumpJobs();
        }
    }

    /// <summary>
    /// Verifies top-level menu-bar items use the dedicated Visual Studio-like menu chrome.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void TopLevelMenuUsesDedicatedMenuBarTheme(ThemeVariant variant)
    {
        var menuItem = new MenuItem { Header = "_Help" };
        var menu = new Menu();
        menu.Items.Add(menuItem);

        var host = ShowControl(variant, menu);
        try
        {
            menuItem.ApplyTemplate();
            PumpJobs();

            var presenter = FindTemplatePart<ContentPresenter>(menuItem, "PART_HeaderPresenter");

            Assert.Multiple(() =>
            {
                Assert.That(menu.Height, Is.EqualTo(30));
                Assert.That(menuItem.MinHeight, Is.EqualTo(30));
                Assert.That(menuItem.Padding, Is.EqualTo(new Thickness(10, 0)));
                Assert.That(presenter.Padding.Top, Is.GreaterThan(presenter.Padding.Bottom));
            });
        }
        finally
        {
            CloseHost(host);
        }
    }

    /// <summary>
    /// Verifies popup menu item rows keep the screenshot-like compact metrics and secondary gesture column.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void PopupMenuItemUsesVisualStudioRowMetrics(ThemeVariant variant)
    {
        var menuItem = new MenuItem
        {
            Header = "_Entry",
            InputGesture = new KeyGesture(Key.F1, KeyModifiers.Control),
        };

        var host = ShowControl(variant, menuItem);
        try
        {
            menuItem.ApplyTemplate();
            PumpJobs();

            var headerPresenter = FindTemplatePart<ContentPresenter>(menuItem, "PART_HeaderPresenter");
            var gestureText = FindTemplatePart<TextBlock>(menuItem, "PART_InputGestureText");
            var iconSlot = FindTemplatePart<ContentControl>(menuItem, "PART_Icon");

            Assert.Multiple(() =>
            {
                Assert.That(menuItem.MinHeight, Is.EqualTo(24));
                Assert.That(menuItem.Padding, Is.EqualTo(new Thickness(0)));
                Assert.That(headerPresenter.Padding, Is.EqualTo(new Thickness(0, 1, 0, 0)));
                Assert.That(gestureText.Margin, Is.EqualTo(new Thickness(20, 0, 8, 0)));
                Assert.That(gestureText.FontSize, Is.EqualTo(12));
                Assert.That(iconSlot.Width, Is.EqualTo(30));
            });
        }
        finally
        {
            CloseHost(host);
        }
    }

    /// <summary>
    /// Verifies menu popup presenters share the menu-specific surface metrics.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void MenuFlyoutPresenterUsesMenuPopupChrome(ThemeVariant variant)
    {
        var presenter = new MenuFlyoutPresenter();

        var host = ShowControl(variant, presenter);
        try
        {
            presenter.ApplyTemplate();
            PumpJobs();

            Assert.Multiple(() =>
            {
                Assert.That(presenter.Padding, Is.EqualTo(new Thickness(4)));
                Assert.That(presenter.CornerRadius, Is.EqualTo(new CornerRadius(6)));
                Assert.That(presenter.MinWidth, Is.EqualTo(220));
            });
        }
        finally
        {
            CloseHost(host);
        }
    }

    /// <summary>
    /// Verifies opaque rounded menu chrome uses a transparent popup host.
    /// </summary>
    [Test]
    public void PopupTransparencyHelperDetectsOpaqueRoundedMenus()
    {
        var submenuChrome = new Border { Name = "PART_MenuPopupChrome" };
        var ordinaryBorder = new Border { Name = "PART_PopupContent" };

        Assert.Multiple(() =>
        {
            Assert.That(PopupTransparencyHelper.UsesOpaqueRoundedMenuChrome(new ContextMenu()), Is.True);
            Assert.That(PopupTransparencyHelper.UsesOpaqueRoundedMenuChrome(new MenuFlyoutPresenter()), Is.True);
            Assert.That(PopupTransparencyHelper.UsesOpaqueRoundedMenuChrome(submenuChrome), Is.True);
            Assert.That(PopupTransparencyHelper.UsesOpaqueRoundedMenuChrome(ordinaryBorder), Is.False);
        });
    }

    private static Window ShowControl(ThemeVariant variant, Control control)
    {
        var window = new Window
        {
            Width = 420,
            Height = 180,
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

    private static void AssertBrushColor(Window window, string key, string expected)
    {
        Assert.That(window.TryFindResource(key, ThemeVariant.Light, out var value), Is.True, $"Resource '{key}' should resolve.");
        Assert.That(value, Is.TypeOf<SolidColorBrush>(), $"Resource '{key}' should be a solid color brush.");

        var brush = (SolidColorBrush)value!;
        Assert.That(brush.Color, Is.EqualTo(Color.Parse(expected)), key);
    }

    private static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
