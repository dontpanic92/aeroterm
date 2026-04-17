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
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// The main window. Acts as a thin composition root that wires
/// <see cref="WindowEffectsService"/>, per-tab <see cref="TerminalSessionCoordinator"/>
/// instances, and the <see cref="TabView"/> / <see cref="TabStrip"/> chrome
/// together with the visual tree.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings settings;
    private readonly WindowEffectsService effectsService;
    private readonly ILogger log;
    private readonly IUpdateService updateService;
    private readonly Grid titleBar;
    private readonly TextBlock titleText;
    private readonly Border terminalBorder;
    private readonly Border tabStripHost;
    private readonly BellService bellService;
    private readonly TabView tabView;
    private readonly TabStrip tabStrip;
    private bool isSettingsDialogOpen;
    private bool isCloseConfirmed;

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
        this.tabStripHost = this.FindControl<Border>("TabStripHost")!;

        this.effectsService = new WindowEffectsService(this, settings, AppLogger.Factory.CreateLogger<WindowEffectsService>());
        this.effectsService.CurrentBackgroundColor = settings.BackgroundColor;
        this.bellService = new BellService(settings, this, this.terminalBorder);

        this.tabView = new TabView();
        this.tabView.ActiveTabChanged += this.OnActiveTabChanged;
        this.tabView.LastTabClosed += this.OnLastTabClosed;
        this.terminalBorder.Child = this.tabView;

        this.tabStrip = new TabStrip { View = this.tabView };
        this.tabStrip.NewTabRequested += this.CreateAndActivateNewTab;
        this.tabStrip.DuplicateTabRequested += this.DuplicateTabFromStrip;
        this.tabStripHost.Child = this.tabStrip;
        this.tabView.Tabs.CollectionChanged += (_, _) => this.UpdateTabStripVisibility();

        this.effectsService.BackgroundBrushChanged += this.OnBackgroundBrushChanged;
        this.effectsService.BackgroundAlphaChanged += this.OnBackgroundAlphaChanged;
        this.settings.PropertyChanged += this.OnSettingsPropertyChanged;

        // Intercept tab-management shortcuts before they reach the focused
        // TerminalControl (whose OnKeyDown forwards everything else to the
        // shell). Tunnel routing fires parent-first during key propagation.
        this.AddHandler(InputElement.KeyDownEvent, this.OnTunnelKeyDown, RoutingStrategies.Tunnel);

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
        // Multi-tab confirm-on-close, unless the user already answered
        // "yes" on an earlier pass through this handler (guard flag reset
        // just before we re-invoke Close()).
        if (!this.isCloseConfirmed
            && this.settings.ConfirmOnClose
            && this.tabView.Tabs.Count > 1)
        {
            e.Cancel = true;
            _ = this.ShowCloseConfirmAndRetryAsync(this.tabView.Tabs.Count);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSWindowMenu.UnregisterWindow(this);
        }

        WindowSettingsPersistence.Capture(this, this.settings);
        this.settings.Save();

        // Dispose every remaining tab (sends SIGHUP to each PTY child).
        var remaining = this.tabView.Tabs.ToArray();
        foreach (var tab in remaining)
        {
            tab.Dispose();
        }

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

    private static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static bool TryDigitKey(Key key, out int zeroBasedIndex)
    {
        if (key >= Key.D1 && key <= Key.D9)
        {
            zeroBasedIndex = key - Key.D1;
            return true;
        }

        if (key >= Key.NumPad1 && key <= Key.NumPad9)
        {
            zeroBasedIndex = key - Key.NumPad1;
            return true;
        }

        zeroBasedIndex = -1;
        return false;
    }

    private async Task ShowCloseConfirmAndRetryAsync(int tabCount)
    {
        bool confirmed;
        try
        {
            var dlg = new Dialogs.ConfirmCloseDialog(tabCount);
            confirmed = await dlg.ShowConfirmAsync(this);
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Confirm-close dialog failed; proceeding with close.");
            confirmed = true;
        }

        if (confirmed)
        {
            this.isCloseConfirmed = true;
            this.Close();
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.HandleTabShortcut(e))
        {
            e.Handled = true;
        }
    }

    private bool HandleTabShortcut(KeyEventArgs e)
    {
        var m = e.KeyModifiers;

        if (IsMac())
        {
            // Cmd+Shift+D — duplicate active tab.
            if (e.Key == Key.D && m == (KeyModifiers.Meta | KeyModifiers.Shift))
            {
                this.DuplicateActiveTab();
                return true;
            }

            // Cmd+T — new tab.
            if (e.Key == Key.T && m == KeyModifiers.Meta)
            {
                this.CreateAndActivateNewTab();
                return true;
            }

            // Cmd+W — close active tab if >1; otherwise fall through (window close handled by OS).
            if (e.Key == Key.W && m == KeyModifiers.Meta)
            {
                if (this.tabView.Tabs.Count > 1 && this.tabView.ActiveTab is { } active)
                {
                    this.tabView.CloseTab(active);
                    return true;
                }

                return false;
            }

            // Ctrl+Tab — next; Ctrl+Shift+Tab — prev.
            if (e.Key == Key.Tab && m.HasFlag(KeyModifiers.Control))
            {
                if (m.HasFlag(KeyModifiers.Shift))
                {
                    this.tabView.ActivatePrev();
                }
                else
                {
                    this.tabView.ActivateNext();
                }

                return true;
            }

            // Cmd+1..9.
            if (m == KeyModifiers.Meta && TryDigitKey(e.Key, out int idx))
            {
                this.tabView.ActivateByIndex(idx);
                return true;
            }
        }
        else
        {
            // Ctrl+Shift+D — duplicate active tab.
            if (e.Key == Key.D && m == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                this.DuplicateActiveTab();
                return true;
            }

            // Ctrl+Shift+T — new tab (Ctrl+T is widely used by shells).
            if (e.Key == Key.T && m == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                this.CreateAndActivateNewTab();
                return true;
            }

            // Ctrl+Shift+W — close tab.
            if (e.Key == Key.W && m == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                if (this.tabView.Tabs.Count > 1 && this.tabView.ActiveTab is { } active)
                {
                    this.tabView.CloseTab(active);
                    return true;
                }

                return false;
            }

            // Ctrl+PageDown — next; Ctrl+PageUp — prev.
            if (m == KeyModifiers.Control && e.Key == Key.PageDown)
            {
                this.tabView.ActivateNext();
                return true;
            }

            if (m == KeyModifiers.Control && e.Key == Key.PageUp)
            {
                this.tabView.ActivatePrev();
                return true;
            }

            // Ctrl+1..9.
            if (m == KeyModifiers.Control && TryDigitKey(e.Key, out int idx))
            {
                this.tabView.ActivateByIndex(idx);
                return true;
            }
        }

        return false;
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
        foreach (var tab in this.tabView.Tabs)
        {
            if (tab.Terminal is not null)
            {
                tab.Terminal.BackgroundAlpha = alpha;
            }
        }
    }

    private TabSession CreateTabSession()
    {
        var session = new TabSession(this.settings);

        // Bell goes to the single window-level BellService regardless of tab.
        if (session.Coordinator is { } coord)
        {
            coord.BellRaised += this.bellService.Handle;
            coord.BackgroundColorChanged += color => this.OnTabBackgroundColorChanged(session, color);
        }

        session.ProcessExitedNormally += () => Dispatcher.UIThread.Post(() => this.OnTabProcessExited(session));
        return session;
    }

    private void CreateAndActivateNewTab()
    {
        var session = this.CreateTabSession();
        this.tabView.AddTab(session);
        this.tabView.ActivateTab(session);

        // Start AFTER activation so the session's Host is visible and
        // Avalonia can give it real layout bounds before StartProcess reads
        // DesiredColCount/DesiredRowCount.
        Dispatcher.UIThread.RunJobs();
        session.Start();
        session.FocusInput();
    }

    private void DuplicateActiveTab()
    {
        if (this.tabView.ActiveTab is { } active)
        {
            this.DuplicateTab(active);
        }
    }

    private void DuplicateTabFromStrip(TabSession source)
    {
        this.DuplicateTab(source);
    }

    private void DuplicateTab(TabSession source)
    {
        TabSession dup;
        try
        {
            dup = this.tabView.DuplicateTab(source);
        }
        catch (ArgumentException)
        {
            // Source is stale (e.g. already closed); nothing to duplicate.
            return;
        }

        // Wire the per-window bell / bg-color / exit plumbing the same way
        // CreateTabSession does for fresh tabs.
        if (dup.Coordinator is { } coord)
        {
            coord.BellRaised += this.bellService.Handle;
            coord.BackgroundColorChanged += color => this.OnTabBackgroundColorChanged(dup, color);
        }

        dup.ProcessExitedNormally += () => Dispatcher.UIThread.Post(() => this.OnTabProcessExited(dup));

        // Start AFTER insertion + activation so the session has real bounds.
        Dispatcher.UIThread.RunJobs();
        dup.Start();
        dup.FocusInput();
    }

    private void OnTabProcessExited(TabSession session)
    {
        if (session.IsDisposed)
        {
            return;
        }

        // If this was the last tab, CloseTab raises LastTabClosed which
        // closes the window; otherwise a neighbour is activated.
        this.tabView.CloseTab(session);
    }

    private void OnTabBackgroundColorChanged(TabSession source, int color)
    {
        // Only the active tab's reported background color affects the window's effects material.
        if (!ReferenceEquals(this.tabView.ActiveTab, source))
        {
            return;
        }

        this.effectsService.CurrentBackgroundColor = color;
        this.effectsService.UpdateBackgroundOpacity();
        this.settings.BackgroundColor = color;
    }

    private void OnActiveTabChanged(TabSession? newActive)
    {
        this.UpdateWindowTitleFromActive();

        // Unsubscribe / re-subscribe title tracking on the active tab.
        foreach (var t in this.tabView.Tabs)
        {
            t.TitleChanged -= this.OnActiveTabTitleChanged;
        }

        if (newActive is not null)
        {
            newActive.TitleChanged += this.OnActiveTabTitleChanged;
            Dispatcher.UIThread.Post(() => newActive.FocusInput(), DispatcherPriority.Input);
        }
    }

    private void OnActiveTabTitleChanged(string title)
    {
        this.UpdateWindowTitleFromActive();
    }

    private void UpdateWindowTitleFromActive()
    {
        var title = this.tabView.ActiveTab?.Title;
        this.Title = string.IsNullOrEmpty(title) ? "AeroTerm" : title;
        this.titleText.Text = this.Title;
    }

    private void UpdateTabStripVisibility()
    {
        bool multi = this.tabView.Tabs.Count > 1;
        this.tabStripHost.IsVisible = multi;

        // When multiple tabs are open, the tab strip is the canonical place
        // to read titles from; collapse the (now-redundant) title text.
        this.titleText.IsVisible = !multi;
    }

    private void OnLastTabClosed()
    {
        this.Close();
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        this.effectsService.DeferMacOSNativeTransparency();

        // Create the initial tab after the window has been measured so the
        // coordinator's PTY gets correct dimensions.
        var initial = this.CreateTabSession();
        this.tabView.AddTab(initial);
        this.tabView.ActivateTab(initial);
        Dispatcher.UIThread.RunJobs();
        initial.Start();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSWindowMenu.RegisterWindow(this);
        }

        // Focus terminal after a brief delay to ensure layout is complete.
        await Task.Delay(100);
        initial.FocusInput();
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
