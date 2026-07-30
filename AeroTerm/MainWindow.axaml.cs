// <copyright file="MainWindow.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using AeroTerm.Controls;
using AeroTerm.Diagnostics;
using AeroTerm.Resources;
using AeroTerm.Services;
using AeroTerm.Theme.Controls;
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
    /// <summary>
    /// Width in DIPs reserved at the leading edge of the custom titlebar
    /// for the macOS native traffic-light cluster (close / minimize / zoom).
    /// Standard Aqua geometry: three 12px buttons, 8px gaps, ~20px left
    /// padding, plus a small visual breathing slot before the next element.
    /// </summary>
    private const double MacChromeReservationWidth = 78.0;

    /// <summary>
    /// Width in DIPs reserved at the trailing edge of the horizontal tab
    /// strip so the user always has a guaranteed empty area to drag the
    /// window or double-click to maximize, even when many tabs would
    /// otherwise consume the entire titlebar width.
    /// </summary>
    private const double TrailingDragReservationWidth = 24.0;

    /// <summary>
    /// Unified custom titlebar height in DIPs. Chosen to match the macOS
    /// "unified / thick" titlebar (Safari, Terminal.app, iTerm2) so the
    /// native traffic-light cluster sits vertically centered against our
    /// tab strip. Identical on Windows / Linux for a consistent look.
    /// </summary>
    private const double TitleBarHeight = 38.0;

    /// <summary>
    /// Height in DIPs of the strip at the very top of the screen that hides
    /// the notch overlay so the macOS menu bar can reveal itself. Kept to
    /// the extreme edge because the tab strip itself sits against that edge.
    /// </summary>
    private const double NotchBarRevealStrip = 2.0;

    /// <summary>
    /// Extra distance in DIPs below the notch band that the pointer must
    /// clear before the overlay is restored, covering the full-screen title
    /// bar that macOS slides in beneath the band.
    /// </summary>
    private const double NotchBarRestoreMargin = 48.0;

    private const double HorizontalTitleBarButtonWidth = 46.0;

    private const double VerticalTitleBarButtonWidth = 28.0;

    private readonly AppSettings settings;
    private readonly WindowEffectsService effectsService;
    private readonly ILogger log;
    private readonly IUpdateService updateService;
    private readonly Grid titleBar;
    private readonly Grid verticalTitleBar;
    private readonly Grid sideRail;
    private readonly ColumnDefinition railColumn;
    private readonly GridSplitter railSplitter;
    private readonly Border terminalTopDragStrip;
    private readonly Border terminalBorder;
    private readonly Border titleBarTabHost;
    private readonly Border sideTabHost;
    private readonly Border horizontalMacChromeHost;
    private readonly Border verticalMacChromeHost;
    private readonly Border horizontalWindowControlsHost;
    private readonly Border verticalWindowControlsHost;
    private readonly Border macChromeReservation;
    private readonly Border titleBarDragHandle;
    private readonly Border titleBarTrailingDragReservation;
    private readonly Border verticalTitleBarDragHandle;
    private readonly DockPanel titleBarTabDock;
    private readonly StackPanel windowControlsPanel;
    private readonly Button settingsButton;
    private readonly Button minimizeButton;
    private readonly Button maximizeButton;
    private readonly Button closeButton;
    private readonly BellService bellService;
    private readonly TabView tabView;
    private readonly TabStrip tabStrip;
    private readonly Dictionary<TabSession, Action> tabUnwire = new();
    private readonly Dictionary<AeroTerm.Controls.ITabSessionContent, Action> paneUnwire
        = new(ReferenceEqualityComparer.Instance);

    private bool isSettingsDialogOpen;
    private bool isCloseConfirmed;
    private bool suppressInitialTab;
    private string closeTrigger = "external-close-request";

    /// <summary>
    /// Last background brush resolved by the effects service, cached so the
    /// chrome can be repainted when the tab-bar orientation changes without
    /// waiting for the next background-change notification.
    /// </summary>
    private IBrush? currentBackgroundBrush;

    /// <summary>
    /// Overlay hosting the tab strip inside the macOS full-screen notch
    /// band, or <c>null</c> when that mode is not active.
    /// </summary>
    private NotchBarWindow? notchBar;

    /// <summary>
    /// Geometry of the band currently occupied by <see cref="notchBar"/>.
    /// </summary>
    private MacNotchBand? notchBand;

    /// <summary>
    /// Pointer poll driving <see cref="PollNotchBarPointer"/>.
    /// </summary>
    private DispatcherTimer? notchBarPollTimer;

    /// <summary>
    /// Whether the overlay is temporarily hidden so the menu bar and the
    /// full-screen title bar can be reached.
    /// </summary>
    private bool isNotchBarSteppedAside;

    /// <summary>
    /// Number of times the overlay's native window level had to be
    /// reapplied, for diagnosing contention with Avalonia's own level
    /// management.
    /// </summary>
    private int notchBarLevelCorrections;

    /// <summary>
    /// Whether the one-shot notch overlay diagnostics have been emitted.
    /// </summary>
    private bool hasLoggedNotchBarLevel;

    /// <summary>
    /// Screen position the overlay is meant to occupy, used to snap it back
    /// if anything moves it.
    /// </summary>
    private PixelPoint? notchBarPosition;

    /// <summary>
    /// Whether the full-screen menu bar / title bar are currently prevented
    /// from auto-revealing, so the option is only pushed on change.
    /// </summary>
    private bool isFullScreenChromeHidden;

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
        this.verticalTitleBar = this.FindControl<Grid>("VerticalTitleBar")!;
        this.sideRail = this.FindControl<Grid>("SideRail")!;
        this.railSplitter = this.FindControl<GridSplitter>("RailSplitter")!;
        this.railColumn = this.FindControl<Grid>("ContentDock")!.ColumnDefinitions[0];
        this.terminalTopDragStrip = this.FindControl<Border>("TerminalTopDragStrip")!;
        this.terminalBorder = this.FindControl<Border>("TerminalBorder")!;
        this.titleBarTabHost = this.FindControl<Border>("TitleBarTabHost")!;
        this.sideTabHost = this.FindControl<Border>("SideTabHost")!;
        this.horizontalMacChromeHost = this.FindControl<Border>("HorizontalMacChromeHost")!;
        this.verticalMacChromeHost = this.FindControl<Border>("VerticalMacChromeHost")!;
        this.horizontalWindowControlsHost = this.FindControl<Border>("HorizontalWindowControlsHost")!;
        this.verticalWindowControlsHost = this.FindControl<Border>("VerticalWindowControlsHost")!;
        this.macChromeReservation = this.FindControl<Border>("MacChromeReservation")!;
        this.verticalTitleBarDragHandle = this.FindControl<Border>("VerticalTitleBarDragHandle")!;
        this.windowControlsPanel = this.FindControl<StackPanel>("WindowControlsPanel")!;
        this.settingsButton = this.FindControl<Button>("SettingsButton")!;
        this.minimizeButton = this.FindControl<Button>("MinimizeButton")!;
        this.maximizeButton = this.FindControl<Button>("MaximizeButton")!;
        this.closeButton = this.FindControl<Button>("CloseButton")!;

        // Wire the full logo cell into the same drag / double-click-to-zoom
        // gesture as the rest of the title bar so users can grab its blank
        // padding as well as the text to move the window.
        var logoDragHandle = this.FindControl<Border>("LogoDragHandle");
        if (logoDragHandle != null)
        {
            logoDragHandle.PointerPressed += this.TitleBar_PointerPressed;
            logoDragHandle.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;
        }

        // Title bar background is transparent;the floating blur effect is
        // rendered by TerminalControl's SkiaSharp pipeline via TopInset.
        this.titleBar.Background = Brushes.Transparent;

        // Transparent drag handle that fills the title-bar slot to the right
        // of the tab strip (or the entire slot in vertical mode). Hosts the
        // window-move-drag gesture so presses on tab pills no longer kick
        // off a window drag and steal pointer capture from the TabStrip's
        // own reorder/detach handlers.
        this.titleBarDragHandle = new Border
        {
            Background = Brushes.Transparent,
            Focusable = false,
        };
        this.titleBarDragHandle.PointerPressed += this.TitleBar_PointerPressed;
        this.titleBarDragHandle.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;
        this.verticalTitleBarDragHandle.PointerPressed += this.TitleBar_PointerPressed;
        this.verticalTitleBarDragHandle.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;

        // Vertical mode has no floating title bar over the terminal, so a
        // fixed-height band above the terminal grid carries the same
        // window-move / zoom gesture.
        this.terminalTopDragStrip.PointerPressed += this.TitleBar_PointerPressed;
        this.terminalTopDragStrip.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;

        this.railSplitter.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        this.railSplitter.DragCompleted += this.RailSplitter_DragCompleted;

        // Fixed-width trailing reservation that guarantees a draggable
        // area on the right edge of the horizontal tab strip even when
        // many tabs would otherwise consume the entire titlebar width.
        this.titleBarTrailingDragReservation = new Border
        {
            Background = Brushes.Transparent,
            Focusable = false,
            Width = TrailingDragReservationWidth,
        };
        this.titleBarTrailingDragReservation.PointerPressed += this.TitleBar_PointerPressed;
        this.titleBarTrailingDragReservation.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;
        this.titleBarTabDock = new DockPanel { LastChildFill = true };

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
        this.tabStrip.NewTabWithProfileRequested += this.CreateAndActivateNewTabFromProfile;
        this.tabStrip.ManageProfilesRequested += () => _ = this.ShowSettingsDialogAsync();
        this.tabStrip.TabReorderRequested += (from, to) => this.tabView.MoveTab(from, to);
        this.tabStrip.TabDetachRequested += this.OnTabDetachRequested;
        this.tabStrip.TabTransferRequested += this.OnTabTransferRequested;
        this.tabStrip.TabGroupAssignmentRequested += this.OnTabGroupAssignmentRequested;
        this.tabStrip.EmptyAreaPointerPressed += this.OnTabStripEmptyAreaPressed;
        this.tabStrip.EmptyAreaDoubleTapped += this.OnTabStripEmptyAreaDoubleTapped;
        this.tabStrip.Profiles = App.Profiles.Profiles;
        this.tabStrip.DefaultProfileId = App.Profiles.DefaultProfileId;
        this.tabStrip.GroupStore = App.TabGroupStore;
        App.ProfilesChanged += this.OnProfilesChanged;
        this.ApplyTabBarOrientation();
        this.tabView.Tabs.CollectionChanged += this.OnTabsCollectionChanged;

        this.effectsService.BackgroundBrushChanged += this.OnBackgroundBrushChanged;
        this.effectsService.BackgroundAlphaChanged += this.OnBackgroundAlphaChanged;
        this.settings.PropertyChanged += this.OnSettingsPropertyChanged;

        // Intercept tab-management shortcuts before they reach the focused
        // TerminalControl (whose OnKeyDown forwards everything else to the
        // shell). Tunnel routing fires parent-first during key propagation.
        this.AddHandler(InputElement.KeyDownEvent, this.OnTunnelKeyDown, RoutingStrategies.Tunnel);

        this.UpdateTitleBarForeground(settings.ForegroundColor);
        this.ApplyTabForegroundFromColorScheme();

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
    /// Gets the tab strip hosted by this window, used by
    /// <see cref="DragDropCoordinator"/> for cross-window drop detection.
    /// </summary>
    internal TabStrip Strip => this.tabStrip;

    /// <summary>
    /// Opens the settings dialog. Called from the macOS native app menu.
    /// </summary>
    public void OpenSettings()
    {
        _ = this.ShowSettingsDialogAsync();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // If settings could not be loaded (corrupt file or a transient read
        // failure while another instance was writing), surface the detailed
        // reason to the user once, then clear it so it doesn't reappear.
        if (!string.IsNullOrEmpty(this.settings.LastPersistenceError))
        {
            _ = this.ShowSettingsLoadErrorAsync(this.settings.LastPersistenceError);
            this.settings.ClearLastPersistenceError();
        }
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.log.LogInformation(
            "Main window closing requested — PID={ProcessId}, Trigger={Trigger}, Tabs={TabCount}, WindowState={WindowState}, Size={Width}x{Height}.",
            Environment.ProcessId,
            this.closeTrigger,
            this.tabView.Tabs.Count,
            this.WindowState,
            this.Width,
            this.Height);

        // Multi-tab confirm-on-close, unless the user already answered
        // "yes" on an earlier pass through this handler (guard flag reset
        // just before we re-invoke Close()).
        if (!this.isCloseConfirmed
            && this.settings.ConfirmOnClose
            && this.tabView.Tabs.Count > 1)
        {
            e.Cancel = true;
            this.log.LogInformation(
                "Main window close deferred for confirmation — PID={ProcessId}, Trigger={Trigger}, Tabs={TabCount}.",
                Environment.ProcessId,
                this.closeTrigger,
                this.tabView.Tabs.Count);
            _ = this.ShowCloseConfirmAndRetryAsync(this.tabView.Tabs.Count);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSWindowMenu.UnregisterWindow(this);
        }

        WindowSettingsPersistence.Capture(this, this.settings);
        this.settings.Save($"main-window-close:{this.closeTrigger}");

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

    private async Task ShowSettingsLoadErrorAsync(string detail)
    {
        try
        {
            await NativeMessageBox.ShowOkAsync(
                this,
                Strings.SettingsLoadErrorTitle,
                detail,
                Strings.ButtonOk);
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Failed to show settings-load-error dialog.");
        }
    }

    private async Task ShowCloseConfirmAndRetryAsync(int tabCount)
    {
        bool confirmed;
        try
        {
            var testOverride = App.TestConfirmCloseHandler;
            if (testOverride is not null)
            {
                confirmed = await testOverride(this);
            }
            else
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.ConfirmCloseMessageFormat,
                    tabCount);
                NativeMessageBoxResult result = await NativeMessageBox.ShowYesNoAsync(
                    this,
                    Strings.ConfirmCloseTitle,
                    message,
                    Strings.ButtonClose,
                    Strings.ButtonCancel);
                confirmed = result == NativeMessageBoxResult.Yes;
            }
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
        // Resolve via central keybindings first — honours user overrides
        // and keeps the bindings visible in the command palette.
        var chord = new KeyChord(e.KeyModifiers, e.Key);
        var resolved = App.Keybindings.Resolve(chord);
        if (resolved?.Action == KeybindingAction.OpenCommandPalette)
        {
            this.OpenCommandPalette();
            return true;
        }

        if (resolved?.Action == KeybindingAction.MoveTabLeft)
        {
            this.tabView.MoveActiveTabLeft();
            return true;
        }

        if (resolved?.Action == KeybindingAction.MoveTabRight)
        {
            this.tabView.MoveActiveTabRight();
            return true;
        }

        if (resolved?.Action == KeybindingAction.GroupNewFromActive)
        {
            this.CreateGroupFromActiveTab();
            return true;
        }

        if (resolved?.Action == KeybindingAction.UngroupActive)
        {
            this.UngroupActiveTab();
            return true;
        }

        if (resolved?.Action == KeybindingAction.SplitPaneHorizontal)
        {
            this.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Horizontal);
            return true;
        }

        if (resolved?.Action == KeybindingAction.SplitPaneVertical)
        {
            this.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Vertical);
            return true;
        }

        if (resolved?.Action == KeybindingAction.FocusPaneLeft)
        {
            this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Left);
            return true;
        }

        if (resolved?.Action == KeybindingAction.FocusPaneRight)
        {
            this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Right);
            return true;
        }

        if (resolved?.Action == KeybindingAction.FocusPaneUp)
        {
            this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Up);
            return true;
        }

        if (resolved?.Action == KeybindingAction.FocusPaneDown)
        {
            this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Down);
            return true;
        }

        if (resolved?.Action == KeybindingAction.ClosePane)
        {
            this.CloseActivePane();
            return true;
        }

        if (resolved?.Action == KeybindingAction.ToggleTabBarOrientation)
        {
            this.ToggleTabBarOrientation();
            return true;
        }

        if (this.settings.EnableWorkbench && resolved?.Action == KeybindingAction.ShowWorkbenchGit)
        {
            this.ShowWorkbenchGit();
            return true;
        }

        if (resolved?.Action == KeybindingAction.JumpToPreviousCommand)
        {
            this.tabView.ActiveTab?.Terminal?.JumpToPreviousCommand();
            return true;
        }

        if (resolved?.Action == KeybindingAction.JumpToNextCommand)
        {
            this.tabView.ActiveTab?.Terminal?.JumpToNextCommand();
            return true;
        }

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
            var pages = ViewModels.SettingsPageFactory.CreateApplicationPages(
                this.settings,
                App.KeybindingStore,
                App.ProfileStore,
                this.updateService);
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
        this.terminalBorder.Background = brush;
        this.currentBackgroundBrush = brush;
        this.ApplyChromeBackground();
    }

    /// <summary>
    /// Paints the vertical-mode chrome — the rail (which also hosts the
    /// caption buttons) and the terminal's top drag strip — with the same
    /// resolved brush the terminal uses, so the window reads as one surface.
    /// Horizontal mode is left transparent: there the floating title bar
    /// renders a blurred slice of terminal content via
    /// <see cref="Controls.TerminalControl.TopInset"/>, which painting over
    /// would hide.
    /// </summary>
    private void ApplyChromeBackground()
    {
        bool vertical = this.settings.TabBarOrientation == TabBarOrientation.Vertical;
        var brush = vertical && this.currentBackgroundBrush is not null
            ? this.currentBackgroundBrush
            : Brushes.Transparent;

        this.sideRail.Background = brush;
        this.terminalTopDragStrip.Background = brush;
    }

    private void OnBackgroundAlphaChanged(byte alpha)
    {
        foreach (var tab in this.tabView.Tabs)
        {
            foreach (var content in tab.AllContents)
            {
                if (content.Terminal is not null)
                {
                    content.Terminal.BackgroundAlpha = alpha;
                }
            }
        }
    }

    private TabSession CreateTabSession()
    {
        var factory = App.TestTabContentFactory;
        TabSession session;
        if (factory is not null)
        {
            session = new TabSession(factory(this.settings));
        }
        else
        {
            var profile = App.Profiles.DefaultProfile ?? ProfileStore.CreateSynthesizedDefault();
            session = new TabSession(this.settings, profile, fallback: null);
        }

        this.WireTabSession(session);
        return session;
    }

    private TabSession CreateTabSessionForProfile(Profile profile)
    {
        var session = new TabSession(this.settings, profile, fallback: null);
        this.WireTabSession(session);
        return session;
    }

    /// <summary>
    /// Subscribes per-window plumbing (bell, bg-color-change, exit) to the
    /// supplied tab and records compensating unsubscribe actions so
    /// <see cref="UnwireTabSession"/> can undo the wiring when the tab
    /// detaches into a different window. Splits inside the tab trigger
    /// per-pane wiring via the session's <c>PaneAdded</c> event.
    /// </summary>
    /// <param name="session">The session to wire into this window.</param>
    private void WireTabSession(TabSession session)
    {
        foreach (var content in session.AllContents)
        {
            this.WirePane(session, content);
        }

        Action<AeroTerm.Controls.ITabSessionContent> onPaneAdded = c => this.WirePane(session, c);
        Action<AeroTerm.Controls.ITabSessionContent> onPaneRemoving = c => this.UnwirePane(c);
        session.PaneAdded += onPaneAdded;
        session.PaneRemoving += onPaneRemoving;

        Action exitHandler = () => Dispatcher.UIThread.Post(() => this.OnTabProcessExited(session));
        session.ProcessExitedNormally += exitHandler;

        this.tabUnwire[session] = () =>
        {
            session.PaneAdded -= onPaneAdded;
            session.PaneRemoving -= onPaneRemoving;
            session.ProcessExitedNormally -= exitHandler;
            foreach (var content in session.AllContents)
            {
                this.UnwirePane(content);
            }
        };
    }

    private void WirePane(TabSession session, AeroTerm.Controls.ITabSessionContent content)
    {
        if (this.paneUnwire.ContainsKey(content))
        {
            return;
        }

        var unwires = new List<Action>();

        if (content.Coordinator is { } coord)
        {
            Action bellHandler = this.bellService.Handle;
            coord.BellRaised += bellHandler;
            unwires.Add(() => coord.BellRaised -= bellHandler);

            Action<int> bgHandler = color => this.OnPaneBackgroundColorChanged(session, content, color);
            coord.BackgroundColorChanged += bgHandler;
            unwires.Add(() => coord.BackgroundColorChanged -= bgHandler);
        }

        this.paneUnwire[content] = () =>
        {
            foreach (var u in unwires)
            {
                u();
            }
        };
    }

    private void UnwirePane(AeroTerm.Controls.ITabSessionContent content)
    {
        if (this.paneUnwire.Remove(content, out var unwire))
        {
            unwire();
        }
    }

    private void UnwireTabSession(TabSession session)
    {
        if (this.tabUnwire.TryGetValue(session, out var unwire))
        {
            unwire();
            this.tabUnwire.Remove(session);
        }
    }

    private void AdoptTab(TabSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.WireTabSession(session);
        this.tabView.AddTab(session);
        this.tabView.ActivateTab(session);
        this.ApplyTopInsetToSession(session);
    }

    /// <summary>
    /// Adopts a tab session from another window, inserting it at the
    /// specified position rather than appending.
    /// </summary>
    /// <param name="session">The tab session to adopt.</param>
    /// <param name="insertionIndex">Zero-based insertion index.</param>
    private void AdoptTabAt(TabSession session, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.WireTabSession(session);
        this.tabView.InsertTab(session, insertionIndex);
        this.tabView.ActivateTab(session);
        this.ApplyTopInsetToSession(session);
    }

    private void OnTabDetachRequested(TabSession tab, PixelPoint screenPos)
    {
        if (this.tabView.Tabs.IndexOf(tab) < 0)
        {
            return;
        }

        // Spawn the new window FIRST (invisible) and hand off the tab
        // before we remove it from our strip. This guarantees the session
        // is never un-parented visually, and that we only close ourselves
        // after the detached window is actually shown.
        var newWindow = new MainWindow(this.settings);
        this.UnwireTabSession(tab);
        this.tabView.DetachTab(tab);

        newWindow.suppressInitialTab = true;
        newWindow.AdoptTab(tab);
        try
        {
            newWindow.Position = screenPos;
        }
        catch
        {
            // Position may throw on platforms where the window is not yet shown;
            // fall back to default placement and rely on the window manager.
        }

        newWindow.Show();

        if (this.tabView.Tabs.Count == 0)
        {
            this.RequestClose("last-tab-detached");
        }
    }

    private void OnTabTransferRequested(TabSession tab, MainWindow targetWindow, int insertionIndex)
    {
        if (this.tabView.Tabs.IndexOf(tab) < 0)
        {
            return;
        }

        this.UnwireTabSession(tab);
        this.tabView.DetachTab(tab);

        targetWindow.AdoptTabAt(tab, insertionIndex);
        targetWindow.Activate();

        if (this.tabView.Tabs.Count == 0)
        {
            this.RequestClose("last-tab-transferred");
        }
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
        this.ApplyTopInsetToSession(session);
        session.FocusInput();
    }

    private void CreateAndActivateNewTabFromProfile(Profile profile)
    {
        var session = this.CreateTabSessionForProfile(profile);
        this.tabView.AddTab(session);
        this.tabView.ActivateTab(session);
        Dispatcher.UIThread.RunJobs();
        session.Start();
        this.ApplyTopInsetToSession(session);
        session.FocusInput();
    }

    private void OnProfilesChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.tabStrip.Profiles = App.Profiles.Profiles;
            this.tabStrip.DefaultProfileId = App.Profiles.DefaultProfileId;
        });
    }

    private void DuplicateActiveTab()
    {
        if (this.tabView.ActiveTab is { } active)
        {
            this.DuplicateTab(active);
        }
    }

    private void SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation orientation)
    {
        if (this.tabView.ActiveTab is not { } active)
        {
            return;
        }

        try
        {
            active.SplitActivePane(orientation);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // Force a layout pass so the new pane's visual host has real
        // bounds before we Start the PTY (mirrors the new-tab path).
        Dispatcher.UIThread.RunJobs();
        active.Start();
        active.FocusInput();
    }

    private void FocusActivePane(AeroTerm.Controls.Panes.PaneDirection direction)
    {
        if (this.tabView.ActiveTab is { } active)
        {
            active.FocusPaneDirection(direction);
        }
    }

    private void CloseActivePane()
    {
        if (this.tabView.ActiveTab is not { } active)
        {
            return;
        }

        bool survived = active.CloseActivePane();
        if (!survived)
        {
            this.tabView.CloseTab(active);
            return;
        }

        // Keep the window title in sync with whichever pane is now focused.
        this.UpdateWindowTitleFromActive();
        active.FocusInput();
    }

    private void CreateGroupFromActiveTab()
    {
        if (this.tabView.ActiveTab is not { } active)
        {
            return;
        }

        var store = App.TabGroupStore;
        int n = store.Groups.Count + 1;
        var group = store.CreateGroup($"Group {n}");
        active.GroupId = group.Id;
    }

    private void UngroupActiveTab()
    {
        if (this.tabView.ActiveTab is { } active)
        {
            active.GroupId = null;
        }
    }

    private void OnTabGroupAssignmentRequested(TabSession tab, string? groupId)
    {
        if (groupId == TabStrip.CreateGroupSentinel)
        {
            var store = App.TabGroupStore;
            int n = store.Groups.Count + 1;
            var group = store.CreateGroup($"Group {n}");
            tab.GroupId = group.Id;
            return;
        }

        tab.GroupId = groupId;
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

        this.WireTabSession(dup);

        // Start AFTER insertion + activation so the session has real bounds.
        Dispatcher.UIThread.RunJobs();
        dup.Start();
        this.ApplyTopInsetToSession(dup);
        dup.FocusInput();
    }

    private void OnTabProcessExited(TabSession session)
    {
        if (session.IsDisposed)
        {
            return;
        }

        this.log.LogInformation(
            "Terminal process exited — PID={ProcessId}, TabTitle={TabTitle}, TabsBeforeClose={TabCount}.",
            Environment.ProcessId,
            session.Title,
            this.tabView.Tabs.Count);

        // If this was the last tab, CloseTab raises LastTabClosed which
        // closes the window; otherwise a neighbour is activated.
        this.tabView.CloseTab(session);
    }

    private void OnPaneBackgroundColorChanged(TabSession source, AeroTerm.Controls.ITabSessionContent paneSource, int color)
    {
        // Only the active pane of the active tab's reported background color
        // affects the window's effects material.
        if (!ReferenceEquals(this.tabView.ActiveTab, source))
        {
            return;
        }

        if (!ReferenceEquals(source.Coordinator, paneSource.Coordinator))
        {
            return;
        }

        this.effectsService.CurrentBackgroundColor = color;
        this.effectsService.UpdateBackgroundOpacity();

        // Note: do not write `color` back into `this.settings.BackgroundColor`.
        // The reported colour is a transient hint derived from what the active
        // pane is currently drawing (e.g. btop fills the alt buffer with its
        // own bg). Persisting it would clobber the user's saved preference and
        // make full-screen TUI apps "stick" their bg across restarts.
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
    }

    private void UpdateTabStripVisibility()
    {
        bool horizontal = this.settings.TabBarOrientation != TabBarOrientation.Vertical;
        this.titleBar.IsVisible = horizontal;
        this.sideRail.IsVisible = !horizontal;
        this.railSplitter.IsVisible = !horizontal;
        this.terminalTopDragStrip.IsVisible = !horizontal;

        // A collapsed child does not shrink its grid column, so the rail
        // column has to be zeroed explicitly in horizontal mode. Min/Max are
        // assigned in an order that never leaves MinWidth above MaxWidth.
        if (horizontal)
        {
            this.railColumn.MinWidth = 0;
            this.railColumn.MaxWidth = 0;
            this.railColumn.Width = new GridLength(0, GridUnitType.Pixel);
        }
        else
        {
            this.railColumn.MaxWidth = AppSettings.MaxVerticalRailWidth;
            this.railColumn.MinWidth = AppSettings.MinVerticalRailWidth;
            this.railColumn.Width = new GridLength(this.settings.VerticalRailWidth, GridUnitType.Pixel);
        }

        this.ApplyChromeBackground();
    }

    /// <summary>
    /// Persists the rail width the user just dragged to. The setter on
    /// <see cref="AppSettings.VerticalRailWidth"/> re-clamps the value, so a
    /// transient out-of-range measurement can never be stored.
    /// </summary>
    private void RailSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        double width = this.railColumn.ActualWidth;
        if (width > 0)
        {
            this.settings.VerticalRailWidth = width;
        }
    }

    private void OnTabsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TabSession removed in e.OldItems)
            {
                // CloseTab disposes the session; detach already unwired in
                // OnTabDetachRequested. Either way, drop any per-window
                // event hookups so they don't fire on a tab we no longer own.
                this.UnwireTabSession(removed);
            }
        }

        this.UpdateTabStripVisibility();
    }

    private void OnLastTabClosed()
    {
        this.RequestClose("last-tab-closed");
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        this.effectsService.DeferMacOSNativeTransparency();

        if (this.suppressInitialTab)
        {
            // Adopted tab path: the session is already inserted + started
            // by the source window. Just defer focus until layout is done.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MacOSWindowMenu.RegisterWindow(this);
            }

            await Task.Delay(100);
            this.tabView.ActiveTab?.FocusInput();
            return;
        }

        // Create the initial tab after the window has been measured so the
        // coordinator's PTY gets correct dimensions.
        var initial = this.CreateTabSession();
        this.tabView.AddTab(initial);
        this.tabView.ActivateTab(initial);
        Dispatcher.UIThread.RunJobs();
        initial.Start();
        this.ApplyTopInsetToSession(initial);

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

    private void TitleBarDragHandle_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Mirror the OS convention: double-click on the title-bar drag
        // region toggles maximize, matching the MaximizeButton click path.
        this.WindowState = this.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }

    /// <summary>
    /// Treats blank space in the vertical rail's tab list as title-bar drag
    /// area. Horizontal mode is skipped: there the strip's spare room is
    /// already covered by the dedicated title-bar drag handles.
    /// </summary>
    /// <param name="e">The originating pointer-press arguments.</param>
    private void OnTabStripEmptyAreaPressed(PointerPressedEventArgs e)
    {
        if (this.settings.TabBarOrientation != TabBarOrientation.Vertical)
        {
            return;
        }

        this.BeginMoveDrag(e);
    }

    /// <summary>
    /// Toggles maximize when the user double-taps blank space in the
    /// vertical rail, matching the title bar's double-click gesture.
    /// </summary>
    private void OnTabStripEmptyAreaDoubleTapped()
    {
        if (this.settings.TabBarOrientation != TabBarOrientation.Vertical)
        {
            return;
        }

        this.WindowState = this.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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
        this.settingsButton.IsVisible = false;
        this.minimizeButton.IsVisible = false;
        this.maximizeButton.IsVisible = false;
        this.closeButton.IsVisible = false;

        // Hide logo on macOS (native title bar shows app name)
        var logoDragHandle = this.FindControl<Border>("LogoDragHandle");
        if (logoDragHandle != null)
        {
            logoDragHandle.IsVisible = false;
        }

        // Reserve leading space so the tab strip / title text never sits
        // underneath the OS-drawn traffic-light cluster. Re-evaluated on
        // every WindowState change because macOS hides the cluster in
        // fullscreen and the reservation should collapse with it.
        this.UpdateMacChromeReservation();
        this.PropertyChanged += this.OnWindowPropertyChangedForMacChrome;

        // Make the empty background area around the native traffic-light
        // cluster draggable / double-click-zoomable. The AppKit-drawn
        // close / minimize / zoom buttons sit above Avalonia content and
        // intercept clicks themselves before these handlers fire.
        this.macChromeReservation.IsHitTestVisible = true;
        this.macChromeReservation.PointerPressed += this.TitleBar_PointerPressed;
        this.macChromeReservation.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;

        // The notch overlay joins every space and outranks the menu bar, so
        // it must not stay on screen once the app is no longer frontmost.
        this.Activated += this.OnActivatedForNotchBar;
        this.Deactivated += this.OnDeactivatedForNotchBar;
        this.Closed += this.OnClosedForNotchBar;
    }

    private void OnActivatedForNotchBar(object? sender, EventArgs e)
    {
        if (this.notchBar is { } bar && !this.isNotchBarSteppedAside && !bar.IsVisible)
        {
            this.ShowNotchBarWindow(bar);
        }
    }

    private void OnDeactivatedForNotchBar(object? sender, EventArgs e)
    {
        // Clicking a tab moves focus to the overlay, which deactivates the
        // main window without the app ever losing frontmost status. Hiding
        // on that would blank the band on the very first click.
        if (MacOSInterop.IsApplicationActive())
        {
            return;
        }

        this.notchBar?.Hide();
    }

    private void OnClosedForNotchBar(object? sender, EventArgs e)
    {
        this.StopNotchBarPolling();

        var bar = this.notchBar;
        this.notchBar = null;
        this.notchBand = null;

        if (bar is not null)
        {
            // Detach before closing so the shared strip is not disposed
            // along with the overlay.
            bar.Host.Child = null;
            bar.Close();
        }
    }

    private void OnWindowPropertyChangedForMacChrome(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            this.UpdateMacChromeReservation();

            // Defer until AppKit finishes its state transition animation
            // (fullscreen space, zoom) so the toolbar / titlebar / glass
            // re-apply lands on a stable NSWindow.
            var state = this.WindowState;
            Dispatcher.UIThread.Post(
                () =>
                {
                    this.effectsService.HandleMacOSWindowStateChanged(state);
                    this.UpdateNotchBar();
                },
                DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Shows or hides the <see cref="NotchBarWindow"/> that hosts the tab
    /// strip inside the band macOS leaves unused above a native full-screen
    /// window on a notched display.
    /// <para>
    /// AppKit clamps the full-screen window to the safe area and paints the
    /// remaining strip black; the clamp cannot be lifted from within the
    /// window. A floating auxiliary overlay can however draw there, so when
    /// <see cref="AppSettings.UseFullScreenNotchArea"/> is enabled the tab
    /// strip is re-parented into that overlay and the in-window title bar
    /// collapses, handing its height back to the terminal.
    /// </para>
    /// </summary>
    private void UpdateNotchBar()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        bool eligible = this.settings.UseFullScreenNotchArea
            && this.WindowState == WindowState.FullScreen
            && this.IsVisible
            && this.settings.TabBarOrientation != TabBarOrientation.Vertical;

        if (!eligible ||
            !MacOSInterop.TryGetFullScreenNotchBand(
                this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
                out var band))
        {
            this.TeardownNotchBar();
            return;
        }

        this.notchBand = band;

        var bar = this.notchBar;
        if (bar is null)
        {
            bar = new NotchBarWindow();
            bar.Opened += (_, _) => bar.ApplyNativeConfiguration();

            // Clicking in the overlay focuses it; hand keyboard focus back so
            // typing keeps reaching the terminal. This must happen on RELEASE,
            // not on activation: activating another window between press and
            // release breaks the pointer capture, so buttons (which need a
            // complete click) would never fire, while tabs — acting on press —
            // still worked.
            bar.AddHandler(
                InputElement.PointerReleasedEvent,
                this.OnNotchBarPointerReleased,
                RoutingStrategies.Bubble,
                handledEventsToo: true);

            // Fallback: the native profile menu runs its own modal loop and
            // swallows the mouse-up, so the release above never arrives when
            // the dropdown is used. Leaving the band hands focus back too.
            bar.AddHandler(
                InputElement.PointerExitedEvent,
                this.OnNotchBarPointerReleased,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            this.notchBar = bar;
        }

        // The strip spans the whole band and flows its tabs around the
        // camera housing, so both the left and right auxiliary areas are
        // usable rather than only the wider one.
        bar.Width = band.ScreenWidth;
        bar.Height = band.Height;
        bar.ApplyBackgroundColor(this.ResolveSchemeBackgroundColor());
        this.tabStrip.NotchGap = (band.NotchLeft, band.NotchRight);

        if (!ReferenceEquals(bar.Host.Child, this.tabStrip))
        {
            this.titleBarTabDock.Children.Remove(this.tabStrip);
            this.titleBarTabHost.Child = null;
            this.sideTabHost.Child = null;
            bar.Host.Child = this.tabStrip;
        }

        // The in-window title bar is now empty; give its height back to the
        // terminal so the full-screen window is used edge to edge.
        this.titleBar.IsVisible = false;
        foreach (var tab in this.tabView.Tabs)
        {
            this.ApplyTopInsetToSession(tab);
        }

        if (!bar.IsVisible)
        {
            bar.Show();
        }

        this.PositionNotchBar(band);
        bar.ApplyNativeConfiguration();
        this.SetFullScreenChromeHidden(true);

        if (!this.hasLoggedNotchBarLevel)
        {
            this.hasLoggedNotchBarLevel = true;
            this.log.LogInformation(
                "Notch overlay active — level={Level} (must exceed the menu bar's 24), band={BandHeight}, notch={NotchLeft}-{NotchRight}, screenWidth={ScreenWidth}.",
                bar.NativeWindowLevel,
                band.Height,
                band.NotchLeft,
                band.NotchRight,
                band.ScreenWidth);
        }

        this.StartNotchBarPolling();
    }

    private void OnNotchBarPointerReleased(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(this.ReturnFocusFromNotchBar, DispatcherPriority.Background);

    /// <summary>
    /// Returns keyboard focus to the main window after the notch overlay has
    /// taken it, so the terminal keeps receiving input when a tab is clicked.
    /// </summary>
    private void ReturnFocusFromNotchBar()
    {
        if (this.notchBar is null || !this.IsVisible)
        {
            return;
        }

        this.Activate();
        this.tabView.ActiveTab?.FocusInput();
    }

    /// <summary>
    /// Prevents (or restores) the auto-revealing macOS full-screen menu bar
    /// and title bar. Applied only on change, since the poll runs at 30ms.
    /// </summary>
    /// <param name="hidden">Whether the chrome should stay hidden.</param>
    private void SetFullScreenChromeHidden(bool hidden)
    {
        if (this.isFullScreenChromeHidden == hidden)
        {
            return;
        }

        if (MacOSInterop.SetFullScreenChromeHidden(
            this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
            hidden))
        {
            this.isFullScreenChromeHidden = hidden;
        }
    }

    /// <summary>
    /// Shows the overlay and re-establishes its geometry and native
    /// configuration.
    /// <para>
    /// Repositioning on every show is required: Avalonia restores its own
    /// notion of the window position when a hidden window is shown again, so
    /// after switching spaces and returning the band would otherwise reappear
    /// floating in the middle of the screen.
    /// </para>
    /// </summary>
    /// <param name="bar">The overlay to show.</param>
    private void ShowNotchBarWindow(NotchBarWindow bar)
    {
        bar.Show();

        if (this.notchBand is { } band)
        {
            this.PositionNotchBar(band);
        }

        bar.ApplyNativeConfiguration();
    }

    /// <summary>
    /// Places the overlay across the top edge of the screen currently
    /// hosting the main window.
    /// </summary>
    /// <param name="band">The resolved band geometry.</param>
    private void PositionNotchBar(MacNotchBand band)
    {
        var bar = this.notchBar;
        if (bar is null)
        {
            return;
        }

        var screen = this.Screens.ScreenFromWindow(this) ?? this.Screens.Primary;
        if (screen is not null)
        {
            this.notchBarPosition = screen.Bounds.TopLeft;
            bar.Position = screen.Bounds.TopLeft;
        }

        bar.Width = band.ScreenWidth;
        bar.Height = band.Height;
    }

    /// <summary>
    /// Returns the tab strip to the in-window title bar and disposes of the
    /// overlay, restoring the standard full-screen / windowed chrome.
    /// </summary>
    private void TeardownNotchBar()
    {
        this.StopNotchBarPolling();
        this.notchBarLevelCorrections = 0;

        // Restore the auto-hide chrome before the window leaves full screen;
        // the presentation options are rejected outside a full-screen space.
        this.SetFullScreenChromeHidden(false);

        var bar = this.notchBar;
        if (bar is null)
        {
            if (!this.titleBar.IsVisible)
            {
                this.titleBar.IsVisible = true;
            }

            return;
        }

        this.notchBar = null;
        this.notchBand = null;
        this.notchBarPosition = null;

        if (ReferenceEquals(bar.Host.Child, this.tabStrip))
        {
            bar.Host.Child = null;
        }

        this.tabStrip.NotchGap = (0, 0);
        this.titleBar.IsVisible = true;

        // Rebuilds the correct host for the current orientation and
        // reapplies the terminal top inset.
        this.ApplyTabBarOrientation();

        bar.Close();
    }

    /// <summary>
    /// Starts the pointer poll that lets the overlay step aside for the
    /// menu bar. The overlay renders above the menu bar and swallows the
    /// hover that would reveal it, so without this the menu bar and the
    /// full-screen title bar would be unreachable while the overlay is up.
    /// </summary>
    private void StartNotchBarPolling()
    {
        if (this.notchBarPollTimer is not null)
        {
            return;
        }

        this.notchBarPollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(30),
            DispatcherPriority.Background,
            (_, _) => this.PollNotchBarPointer());
        this.notchBarPollTimer.Start();
    }

    private void StopNotchBarPolling()
    {
        this.notchBarPollTimer?.Stop();
        this.notchBarPollTimer = null;
        this.isNotchBarSteppedAside = false;
    }

    /// <summary>
    /// Hides the overlay while the pointer rests against the very top edge
    /// of the screen, which lets macOS reveal the menu bar and the
    /// full-screen title bar underneath, and restores it once the pointer
    /// has moved clear of that region again.
    /// </summary>
    private void PollNotchBarPointer()
    {
        var bar = this.notchBar;
        if (bar is null || this.notchBand is not { } band)
        {
            return;
        }

        var mouse = MacOSInterop.GetMouseLocation();

        // AppKit reports a bottom-left origin, so distance from the top of
        // the hosting screen is measured downwards from its top edge.
        double fromTop = band.ScreenTopY - mouse.Y;
        bool onThisScreen = mouse.X >= band.ScreenLeftX
            && mouse.X <= band.ScreenLeftX + band.ScreenWidth;

        if (!this.isNotchBarSteppedAside)
        {
            this.SetFullScreenChromeHidden(true);

            // Snap back if anything moved the band — switching spaces can
            // leave Avalonia restoring a stale position.
            if (this.notchBarPosition is { } expected && bar.Position != expected)
            {
                bar.Position = expected;
            }

            // Keep the overlay above the menu bar: Avalonia resets the
            // NSWindow level behind our back, after which the menu bar would
            // reveal on top of the tab strip.
            if (bar.EnsureNativeConfiguration())
            {
                this.notchBarLevelCorrections++;
                if (this.notchBarLevelCorrections == 1)
                {
                    this.log.LogInformation(
                        "Notch overlay window level was reset externally; reapplied.");
                }
            }

            if (onThisScreen && fromTop >= 0 && fromTop <= NotchBarRevealStrip)
            {
                this.isNotchBarSteppedAside = true;
                this.SetFullScreenChromeHidden(false);
                bar.Hide();
            }

            return;
        }

        // Restore only once the pointer has left the whole reveal region —
        // the band plus the title bar macOS slides in beneath it — so the
        // overlay does not flicker back over the menu bar in use.
        if (!onThisScreen || fromTop > band.Height + NotchBarRestoreMargin)
        {
            this.isNotchBarSteppedAside = false;
            this.ShowNotchBarWindow(bar);
        }
    }

    private void UpdateMacChromeReservation()
    {
        // No traffic lights to dodge in fullscreen — they are hidden by the
        // OS — so let the tab strip reclaim the full titlebar width.
        bool fullscreen = this.WindowState == WindowState.FullScreen;
        this.macChromeReservation.Width = fullscreen ? 0 : MacChromeReservationWidth;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ForegroundColor))
        {
            Dispatcher.UIThread.Post(() => this.UpdateTitleBarForeground(this.settings.ForegroundColor));
        }
        else if (e.PropertyName == nameof(AppSettings.ColorSchemeName))
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.ApplyTabForegroundFromColorScheme();
                this.notchBar?.ApplyBackgroundColor(this.ResolveSchemeBackgroundColor());
            });
        }
        else if (e.PropertyName == nameof(AppSettings.TabBarOrientation))
        {
            Dispatcher.UIThread.Post(this.ApplyTabBarOrientation);
        }
        else if (e.PropertyName == nameof(AppSettings.UseFullScreenNotchArea))
        {
            Dispatcher.UIThread.Post(this.UpdateNotchBar);
        }
    }

    private void ToggleTabBarOrientation()
    {
        this.settings.TabBarOrientation = this.settings.TabBarOrientation == TabBarOrientation.Vertical
            ? TabBarOrientation.Horizontal
            : TabBarOrientation.Vertical;
    }

    private void ShowWorkbenchGit()
    {
        this.tabView.ActiveTab?.ShowGitView();
    }

    private void ApplyTabBarOrientation()
    {
        bool vertical = this.settings.TabBarOrientation == TabBarOrientation.Vertical;
        this.tabStrip.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;

        this.titleBar.Height = TitleBarHeight;
        this.verticalTitleBar.Height = TitleBarHeight;

        // Re-parent the single TabStrip into the orientation-appropriate
        // host, together with the shared native-chrome reservation and
        // custom window-control panel.
        this.titleBarTabDock.Children.Clear();
        if (vertical)
        {
            this.sideTabHost.Child = this.tabStrip;
            this.titleBarTabHost.Child = null;
            this.MoveTitleBarChrome(
                this.verticalMacChromeHost,
                this.verticalWindowControlsHost,
                VerticalTitleBarButtonWidth);
        }
        else
        {
            this.sideTabHost.Child = null;
            this.MoveTitleBarChrome(
                this.horizontalMacChromeHost,
                this.horizontalWindowControlsHost,
                HorizontalTitleBarButtonWidth);

            // Order matters: DockPanel measures children in declaration
            // order. Adding the trailing reservation FIRST (Dock.Right)
            // subtracts its width from the available space the tab strip
            // is measured against, guaranteeing the reservation is never
            // squeezed to zero by a wide row of tabs.
            DockPanel.SetDock(this.titleBarTrailingDragReservation, Dock.Right);
            this.titleBarTabDock.Children.Add(this.titleBarTrailingDragReservation);

            DockPanel.SetDock(this.tabStrip, Dock.Left);
            this.titleBarTabDock.Children.Add(this.tabStrip);

            // LastChildFill: the empty area between the tab strip's
            // trailing edge and the fixed reservation also drags / zooms.
            this.titleBarTabDock.Children.Add(this.titleBarDragHandle);
            this.titleBarTabHost.Child = this.titleBarTabDock;
            this.titleBarTabHost.IsVisible = true;
        }

        // Update TopInset on all existing terminal controls to match
        // the current orientation. Horizontal mode: terminals render a
        // blurred preview in the top inset area behind the floating
        // title bar. Vertical mode: no inset, terminals start at the top
        // (except under the macOS camera housing in full screen).
        foreach (var tab in this.tabView.Tabs)
        {
            this.ApplyTopInsetToSession(tab);
        }

        if (this.notchBar is not null)
        {
            this.UpdateNotchBar();
        }

        this.UpdateTabStripVisibility();
    }

    private void MoveTitleBarChrome(Border macHost, Border controlsHost, double buttonWidth)
    {
        this.horizontalMacChromeHost.Child = null;
        this.verticalMacChromeHost.Child = null;
        macHost.Child = this.macChromeReservation;

        this.horizontalWindowControlsHost.Child = null;
        this.verticalWindowControlsHost.Child = null;
        controlsHost.Child = this.windowControlsPanel;

        this.settingsButton.Width = buttonWidth;
        this.minimizeButton.Width = buttonWidth;
        this.maximizeButton.Width = buttonWidth;
        this.closeButton.Width = buttonWidth;

        this.ApplyWindowControlPlacement(ReferenceEquals(controlsHost, this.verticalWindowControlsHost));
    }

    /// <summary>
    /// Positions and orders the custom caption buttons for the given tab-bar
    /// orientation. In vertical mode the buttons hug the left edge of the
    /// rail and run close, maximize, minimize, settings — mirroring the
    /// horizontal layout so the close button stays on the window's outer
    /// corner. macOS is skipped: the native traffic lights own column 0 and
    /// the custom buttons are hidden there anyway.
    /// </summary>
    /// <param name="vertical">
    /// <see langword="true"/> when the caption buttons live in the vertical
    /// rail's title bar.
    /// </param>
    private void ApplyWindowControlPlacement(bool vertical)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        Grid.SetColumn(this.verticalWindowControlsHost, vertical ? 0 : 2);
        Grid.SetColumn(this.verticalMacChromeHost, vertical ? 2 : 0);

        // Children are reordered in place; the panel itself is shared and
        // re-parented between hosts, so this must run on every switch.
        Button[] order = vertical
            ? [this.closeButton, this.maximizeButton, this.minimizeButton, this.settingsButton]
            : [this.settingsButton, this.minimizeButton, this.maximizeButton, this.closeButton];

        for (int i = 0; i < order.Length; i++)
        {
            int current = this.windowControlsPanel.Children.IndexOf(order[i]);
            if (current >= 0 && current != i)
            {
                this.windowControlsPanel.Children.Move(current, i);
            }
        }
    }

    /// <summary>
    /// Sets <see cref="Controls.TerminalControl.TopInset"/> on every
    /// terminal in <paramref name="session"/> based on the current tab-bar
    /// orientation. Must be called <em>after</em>
    /// <see cref="TabSession.Start"/> so that the TerminalControl exists.
    /// </summary>
    private void ApplyTopInsetToSession(TabSession session)
    {
        bool horizontal = this.settings.TabBarOrientation != TabBarOrientation.Vertical;

        // Horizontal: clear the floating title bar. Vertical: the title bar
        // lives in the side rail, so the terminal starts at the top. When
        // the strip has moved into the macOS notch overlay the in-window
        // title bar is hidden, so there is nothing to clear either.
        float inset = horizontal && this.notchBar is null
            ? (float)TitleBarHeight
            : 0f;
        foreach (var content in session.AllContents)
        {
            if (content.Terminal is not null)
            {
                content.Terminal.TopInset = inset;
            }
        }
    }

    private void UpdateTitleBarForeground(int rgb)
    {
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);

        // Intentionally derived from the active terminal background/foreground
        // contrast instead of the global theme tokens.
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));

        this.Resources["TitleBarForegroundBrush"] = brush;

        // Derive the hover / pressed background tints from the same
        // foreground colour the tab strip uses, mirroring TabStrip's
        // inactive/active hover-tint approach (foreground RGB at a low
        // alpha). This keeps the titlebar buttons readable on both dark
        // and light colour schemes without a separate dark-mode branch.
        const byte hoverAlpha = 0x22;
        const byte pressedAlpha = 0x45;
        this.Resources["TitleBarButtonHoverBrush"] =
            new SolidColorBrush(Color.FromArgb(hoverAlpha, r, g, b));
        this.Resources["TitleBarButtonPressedBrush"] =
            new SolidColorBrush(Color.FromArgb(pressedAlpha, r, g, b));
    }

    /// <summary>
    /// Returns the active colour scheme's background colour, used to paint
    /// the notch overlay opaquely so the menu bar cannot show through it.
    /// </summary>
    private Color ResolveSchemeBackgroundColor()
    {
        var scheme = Models.ColorSchemePresets.FindByName(this.settings.ColorSchemeName)
            ?? Models.ColorSchemePresets.Default;
        int rgb = scheme.Background;
        return Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }

    private void ApplyTabForegroundFromColorScheme()
    {
        var scheme = Models.ColorSchemePresets.FindByName(this.settings.ColorSchemeName)
            ?? Models.ColorSchemePresets.Default;
        this.tabStrip.ApplyForegroundColor(scheme.Foreground);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.RequestClose("titlebar-close-button");
    }

    private void RequestClose(string trigger)
    {
        this.closeTrigger = trigger;
        this.Close();
    }
}
