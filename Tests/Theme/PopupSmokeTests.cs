// <copyright file="PopupSmokeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using NUnit.Framework;

/// <summary>
/// Headless smoke tests for popup-bearing AeroTerm themed controls.
/// </summary>
[TestFixture]
public class PopupSmokeTests
{
    private static readonly (string Name, Action<ThemeVariant> Exercise)[] PopupCases =
    [
        ("ComboBox", ExerciseComboBox),
        ("ContextMenu", ExerciseContextMenu),
        ("MenuFlyout", ExerciseMenuFlyout),
        ("ToolTip", ExerciseToolTip),
        ("SplitButtonFlyout", ExerciseSplitButtonFlyout),
    ];

    /// <summary>
    /// Gets popup-bearing controls paired with light and dark theme variants.
    /// </summary>
    public static IEnumerable<TestCaseData> PopupTestCases
    {
        get
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                foreach (var (name, exercise) in PopupCases)
                {
                    yield return new TestCaseData(variant, name, exercise).SetName($"PopupSmoke_{variant}_{name}");
                }
            }
        }
    }

    /// <summary>
    /// Opens a popup-bearing control and pumps the headless dispatcher.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    /// <param name="popupName">The display name of the popup scenario.</param>
    /// <param name="exercise">The scenario body.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(PopupTestCases))]
    public void PopupContentOpens(ThemeVariant variant, string popupName, Action<ThemeVariant> exercise)
    {
        Assert.DoesNotThrow(() => exercise(variant), $"{popupName} popup should open and render without exceptions.");
    }

    private static Window CreateHost(ThemeVariant variant, Control content)
    {
        return new Window
        {
            Width = 360,
            Height = 240,
            RequestedThemeVariant = variant,
            Content = new VisualLayerManager
            {
                EnableOverlayLayer = true,
                Child = content,
            },
        };
    }

    private static void ShowHost(Window window)
    {
        window.Show();
        window.ApplyTemplate();
        PumpJobs();
        if (window.Content is Control host)
        {
            host.ApplyTemplate();
            PumpJobs();
        }
    }

    private static void ExerciseComboBox(ThemeVariant variant)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "One", "Two", "Three" },
            SelectedIndex = 0,
        };
        var window = CreateHost(variant, comboBox);

        try
        {
            ShowHost(window);
            comboBox.ApplyTemplate();
            comboBox.IsDropDownOpen = true;
            PumpJobs();

            Assert.That(comboBox.IsDropDownOpen, Is.True);
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            window.Close();
            PumpJobs();
        }
    }

    private static void ExerciseContextMenu(ThemeVariant variant)
    {
        var target = new Button { Content = "Target" };
        var contextMenu = new ContextMenu
        {
            ItemsSource = new[] { new MenuItem { Header = "One" } },
        };
        target.ContextMenu = contextMenu;
        var window = CreateHost(variant, target);

        try
        {
            ShowHost(window);
            contextMenu.Open(target);
            PumpJobs();

            Assert.That(contextMenu.IsOpen, Is.True);
        }
        finally
        {
            contextMenu.Close();
            window.Close();
            PumpJobs();
        }
    }

    private static void ExerciseMenuFlyout(ThemeVariant variant)
    {
        var target = new Button { Content = "Target" };
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "One" });
        var window = CreateHost(variant, target);

        try
        {
            ShowHost(window);
            flyout.ShowAt(target);
            PumpJobs();
        }
        finally
        {
            flyout.Hide();
            window.Close();
            PumpJobs();
        }
    }

    private static void ExerciseToolTip(ThemeVariant variant)
    {
        var target = new Button { Content = "Hover target" };
        var toolTip = new ToolTip
        {
            Content = new TextBlock { Text = "Helpful text" },
        };
        ToolTip.SetTip(target, toolTip);
        var window = CreateHost(variant, target);

        try
        {
            ShowHost(window);
            ToolTip.SetIsOpen(target, true);
            PumpJobs();

            Assert.That(ToolTip.GetIsOpen(target), Is.True);
        }
        finally
        {
            ToolTip.SetIsOpen(target, false);
            window.Close();
            PumpJobs();
        }
    }

    private static void ExerciseSplitButtonFlyout(ThemeVariant variant)
    {
        var flyout = new Flyout
        {
            Content = new TextBlock { Text = "Split flyout content" },
        };
        var splitButton = new SplitButton
        {
            Content = "Split",
            Flyout = flyout,
        };
        var window = CreateHost(variant, splitButton);

        try
        {
            ShowHost(window);
            splitButton.ApplyTemplate();
            flyout.ShowAt(splitButton);
            PumpJobs();
        }
        finally
        {
            flyout.Hide();
            window.Close();
            PumpJobs();
        }
    }

    private static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
