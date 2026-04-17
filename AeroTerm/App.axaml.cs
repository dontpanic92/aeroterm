// <copyright file="App.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Linq;
using System.Runtime.InteropServices;
using AeroTerm.Services;
using AeroTerm.WindowEffects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

/// <summary>
/// The Avalonia application.
/// </summary>
public class App : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                MacOSWindowMenu.SetNewWindowHandler(() => this.CreateNewWindow());
            }

            desktop.MainWindow = this.CreateNewWindow();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this.SetupMacOSActivation();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates and shows a new terminal window.
    /// </summary>
    /// <returns>The newly created window.</returns>
    public MainWindow CreateNewWindow()
    {
        var window = new MainWindow(AppSettings.Default);
        window.Show();
        return window;
    }

    /// <summary>
    /// Handles the New Window menu item click from the macOS native app menu.
    /// </summary>
    private void OnNewWindowClicked(object? sender, EventArgs e)
    {
        this.CreateNewWindow();
    }

    /// <summary>
    /// Handles the Settings menu item click from the macOS native app menu.
    /// </summary>
    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var target = desktop.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                         ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
            target?.OpenSettings();
        }
    }

    /// <summary>
    /// On macOS, reopens a window when the app is activated with no windows
    /// (e.g. clicking the dock icon).
    /// </summary>
    private void SetupMacOSActivation()
    {
        if (this.ApplicationLifetime is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, args) =>
            {
                if (args.Kind == ActivationKind.Reopen
                    && this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
                    && !d.Windows.OfType<MainWindow>().Any())
                {
                    this.CreateNewWindow();
                }
            };
        }
    }
}
