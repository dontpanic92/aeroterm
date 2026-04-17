// <copyright file="MainWindow.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Runtime.InteropServices;
using AeroTerm.Controls;
using AeroTerm.Diagnostics;
using AeroTerm.Services;
using AeroTerm.WindowEffects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// The main window. Acts as a thin composition root that wires
/// <see cref="WindowEffectsService"/> and <see cref="TerminalSessionCoordinator"/>
/// together with the visual tree.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings settings;
    private readonly WindowEffectsService effectsService;
    private readonly TerminalSessionCoordinator coordinator;
    private readonly ILogger log;
    private readonly IUpdateService updateService;
    private readonly Grid titleBar;
    private readonly TextBlock titleText;
    private readonly Border terminalBorder;
    private readonly BellService bellService;
    private bool isSettingsDialogOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// Used by the XAML designer; runtime code should use
    /// <see cref="MainWindow(AppSettings)"/>.
    /// </summary>
    public MainWindow()
        : this(AppSettings.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public MainWindow(AppSettings settings)
    {
        this.settings = settings;
        this.log = AppLogger.For<MainWindow>();
        this.updateService = new UpdateService(settings);
        this.InitializeComponent();

        this.titleBar = this.FindControl<Grid>("TitleBar")!;
        this.titleText = this.FindControl<TextBlock>("TitleText")!;
        this.terminalBorder = this.FindControl<Border>("TerminalBorder")!;

        this.effectsService = new WindowEffectsService(this, settings, AppLogger.Factory.CreateLogger<WindowEffectsService>());
        this.effectsService.CurrentBackgroundColor = settings.BackgroundColor;
        this.coordinator = new TerminalSessionCoordinator(settings);
        this.bellService = new BellService(settings, this, this.terminalBorder);

        this.effectsService.BackgroundBrushChanged += this.OnBackgroundBrushChanged;
        this.effectsService.BackgroundAlphaChanged += this.OnBackgroundAlphaChanged;
        this.coordinator.TerminalReady += this.OnTerminalReady;
        this.coordinator.TitleChanged += this.OnTitleChanged;
        this.coordinator.BackgroundColorChanged += this.OnBackgroundColorChanged;
        this.coordinator.ProcessExitedNormally += this.OnProcessExitedNormally;
        this.coordinator.BellRaised += this.bellService.Handle;
        this.settings.PropertyChanged += this.OnSettingsPropertyChanged;

        this.UpdateTitleBarForeground(settings.ForegroundColor);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            this.SetupMacOSTitleBar();
            this.Activated += (s, e) => this.effectsService.HandleMacOSActivation();
        }

        this.effectsService.SetupBlurBehind();
        WindowSettingsPersistence.Apply(this, settings);

        this.Opened += this.OnWindowOpened;
    }

    /// <summary>
    /// Opens the settings dialog. Called from the macOS native app menu.
    /// </summary>
    public void OpenSettings()
    {
        _ = this.ShowSettingsDialogAsync();
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSWindowMenu.UnregisterWindow(this);
        }

        WindowSettingsPersistence.Capture(this, this.settings);
        this.settings.Save();
        this.coordinator.Shutdown();
        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+Comma opens settings
        if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = this.ShowSettingsDialogAsync();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task ShowSettingsDialogAsync()
    {
        if (this.isSettingsDialogOpen)
        {
            return;
        }

        this.isSettingsDialogOpen = true;
        IntPtr blurHandle = this.effectsService.BeginDialogBlurPreservation();

        try
        {
            var pages = new ViewModels.SettingsPageViewModel[]
            {
                new ViewModels.AppearancePageViewModel(this.settings),
                new ViewModels.UpdatesPageViewModel(this.settings, this.updateService),
            };
            var viewModel = new ViewModels.SettingsViewModel(pages);
            var dialog = new Dialogs.SettingsWindow(this.settings, viewModel);
            await dialog.ShowDialog(this);
        }
        finally
        {
            this.effectsService.EndDialogBlurPreservation(blurHandle);
            this.isSettingsDialogOpen = false;
        }
    }

    private void OnBackgroundBrushChanged(IBrush brush)
    {
        this.titleBar.Background = brush;
        this.terminalBorder.Background = brush;
    }

    private void OnBackgroundAlphaChanged(byte alpha)
    {
        if (this.coordinator.Control is not null)
        {
            this.coordinator.Control.BackgroundAlpha = alpha;
        }
    }

    private void OnTerminalReady(TerminalControl control)
    {
        this.terminalBorder.Child = control;
        control.Focus();
    }

    private void OnTitleChanged(string title)
    {
        this.Title = string.IsNullOrEmpty(title) ? "AeroTerm" : title;
        this.titleText.Text = this.Title;
    }

    private void OnBackgroundColorChanged(int color)
    {
        this.effectsService.CurrentBackgroundColor = color;
        this.effectsService.UpdateBackgroundOpacity();
        this.settings.BackgroundColor = color;
    }

    private void OnProcessExitedNormally()
    {
        this.Close();
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        this.effectsService.DeferMacOSNativeTransparency();
        this.coordinator.Initialize();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSWindowMenu.RegisterWindow(this);
        }

        // Focus terminal after a brief delay to ensure layout is complete
        await Task.Delay(100);
        this.coordinator.Control?.Focus();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = this.ShowSettingsDialogAsync();
    }

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.WindowState = this.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void SetupMacOSTitleBar()
    {
        // Hide custom titlebar buttons on macOS (uses native traffic lights)
        var settingsBtn = this.FindControl<Button>("SettingsButton");
        var minimizeBtn = this.FindControl<Button>("MinimizeButton");
        var maximizeBtn = this.FindControl<Button>("MaximizeButton");
        var closeBtn = this.FindControl<Button>("CloseButton");
        if (settingsBtn != null)
        {
            settingsBtn.IsVisible = false;
        }

        if (minimizeBtn != null)
        {
            minimizeBtn.IsVisible = false;
        }

        if (maximizeBtn != null)
        {
            maximizeBtn.IsVisible = false;
        }

        if (closeBtn != null)
        {
            closeBtn.IsVisible = false;
        }

        // Hide logo text on macOS (native title bar shows app name)
        var logoText = this.FindControl<TextBlock>("LogoText");
        if (logoText != null)
        {
            logoText.IsVisible = false;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ForegroundColor))
        {
            Dispatcher.UIThread.Post(() => this.UpdateTitleBarForeground(this.settings.ForegroundColor));
        }
    }

    private void UpdateTitleBarForeground(int rgb)
    {
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));

        this.Resources["TitleBarForegroundBrush"] = brush;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }
}
