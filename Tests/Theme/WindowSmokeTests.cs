// <copyright file="WindowSmokeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System;
using System.Collections.Generic;
using System.Linq;
using AeroTerm.Dialogs;
using AeroTerm.Services;
using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;

/// <summary>
/// Headless smoke tests for application windows under AeroTerm theme variants.
/// </summary>
[TestFixture]
public class WindowSmokeTests
{
    /// <summary>
    /// Gets application windows paired with light and dark theme variants.
    /// </summary>
    public static IEnumerable<TestCaseData> WindowCases
    {
        get
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                yield return new TestCaseData(variant, "SettingsWindow", new Func<Window>(CreateSettingsWindow))
                    .SetName($"WindowSmoke_{variant}_SettingsWindow");
                yield return new TestCaseData(variant, "CommandPaletteWindow", new Func<Window>(() => new CommandPaletteWindow()))
                    .SetName($"WindowSmoke_{variant}_CommandPaletteWindow");
                yield return new TestCaseData(variant, "QuakeWindow", new Func<Window>(() => new QuakeWindow(new AppSettings())))
                    .SetName($"WindowSmoke_{variant}_QuakeWindow");
                yield return new TestCaseData(variant, "FontPickerWindow", new Func<Window>(() => new FontPickerWindow()))
                    .SetName($"WindowSmoke_{variant}_FontPickerWindow");
            }
        }
    }

    /// <summary>
    /// Installs a fake tab-content factory so <see cref="QuakeWindow"/> does not spawn a real shell.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        App.TestTabContentFactory = _ => new FakeTabContent("AeroTerm");
    }

    /// <summary>
    /// Clears test seams that are shared by application windows.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        App.TestTabContentFactory = null;
    }

    /// <summary>
    /// Verifies an application window can instantiate, show, and apply its template.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    /// <param name="windowName">The display name of the window under test.</param>
    /// <param name="factory">Factory that creates the window under test.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(WindowCases))]
    public void WindowAppliesTemplate(ThemeVariant variant, string windowName, Func<Window> factory)
    {
        var window = factory();
        window.RequestedThemeVariant = variant;

        try
        {
            window.Show();
            PumpJobs();
            window.ApplyTemplate();
            PumpJobs();

            Assert.That(window.GetVisualChildren().Any(), Is.True, $"{windowName} should produce visual children.");
        }
        finally
        {
            window.Close();
            PumpJobs();
        }
    }

    private static Window CreateSettingsWindow()
    {
        var settings = new AppSettings();
        var pages = new SettingsPageViewModel[]
        {
            new AppearancePageViewModel(settings),
            new UpdatesPageViewModel(settings, new UpdateService(settings)),
        };
        return new SettingsWindow(settings, new SettingsViewModel(pages));
    }

    private static void PumpJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }
}
