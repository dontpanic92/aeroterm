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

    private readonly AppSettings settings;
    private readonly WindowEffectsService effectsService;
    private readonly ILogger log;
    private readonly IUpdateService updateService;
    private readonly Grid titleBar;
    private readonly TextBlock titleText;
    private readonly Border terminalBorder;
    private readonly Border titleBarTabHost;
    private readonly Border sideTabHost;
    private readonly Border macChromeReservation;
    private readonly Border titleBarDragHandle;
    private readonly Border titleBarTrailingDragReservation;
    private readonly DockPanel titleBarTabDock;
    private readonly BellService bellService;
    private readonly TabView tabView;
    private readonly TabStrip tabStrip;
    private readonly Dictionary<TabSession, Action> tabUnwire = new();
    private readonly Dictionary<AeroTerm.Controls.ITabSessionContent, Action> paneUnwire
        = new(ReferenceEqualityComparer.Instance);

    private bool isSettingsDialogOpen;
    private bool isCloseConfirmed;
    private bool suppressInitialTab;

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
        this.titleBarTabHost = this.FindControl<Border>("TitleBarTabHost")!;
        this.sideTabHost = this.FindControl<Border>("SideTabHost")!;
        this.macChromeReservation = this.FindControl<Border>("MacChromeReservation")!;

        // Wire the logo TextBlock into the same drag / double-click-to-zoom
        // gesture as the rest of the title bar so users can grab the logo
        // to move the window.
        var logoText = this.FindControl<TextBlock>("LogoText");
        if (logoText != null)
        {
            logoText.PointerPressed += this.TitleBar_PointerPressed;
            logoText.DoubleTapped += this.TitleBarDragHandle_DoubleTapped;
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
        this.tabStrip.Profiles = App.Profiles.Profiles;
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
            var pages = new ViewModels.SettingsPageViewModel[]
            {
                new ViewModels.AppearancePageViewModel(this.settings),
                new ViewModels.KeybindingsPageViewModel(App.KeybindingStore),
                new ViewModels.ProfilesPageViewModel(App.ProfileStore),
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
        this.terminalBorder.Background = brush;
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
            this.Close();
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
            this.Close();
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
        this.titleText.Text = this.Title;
    }

    private void UpdateTabStripVisibility()
    {
        // The tab strip is always visible (even with a single tab). When
        // horizontal it lives in the titlebar; when vertical it lives in
        // the side rail. ApplyTabBarOrientation owns which host is shown
        // and parents the strip; here we only sync the title-text label.
        bool horizontal = this.settings.TabBarOrientation != TabBarOrientation.Vertical;

        // Horizontal: tabs in the titlebar already convey the active title,
        // so collapse the redundant TitleText. Vertical: titlebar has no
        // tabs, so show the title.
        this.titleText.IsVisible = !horizontal;
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
        this.Close();
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
    }

    private void OnWindowPropertyChangedForMacChrome(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            this.UpdateMacChromeReservation();

            // Defer until AppKit finishes its full-screen animation so the
            // toolbar detach / reattach lands on a stable NSWindow state.
            bool fullscreen = this.WindowState == WindowState.FullScreen;
            Dispatcher.UIThread.Post(
                () => this.effectsService.HandleMacOSFullScreenTransition(fullscreen),
                DispatcherPriority.Background);
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
            Dispatcher.UIThread.Post(this.ApplyTabForegroundFromColorScheme);
        }
        else if (e.PropertyName == nameof(AppSettings.TabBarOrientation))
        {
            Dispatcher.UIThread.Post(this.ApplyTabBarOrientation);
        }
    }

    private void ToggleTabBarOrientation()
    {
        this.settings.TabBarOrientation = this.settings.TabBarOrientation == TabBarOrientation.Vertical
            ? TabBarOrientation.Horizontal
            : TabBarOrientation.Vertical;
    }

    private void ApplyTabBarOrientation()
    {
        bool vertical = this.settings.TabBarOrientation == TabBarOrientation.Vertical;
        this.tabStrip.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;

        // Single titlebar height for both orientations so the macOS native
        // traffic-light cluster (centered by AppKit inside the unified
        // titlebar region) lines up with our tab strip / title text on
        // every platform.
        this.titleBar.Height = TitleBarHeight;

        // Re-parent the single TabStrip into the orientation-appropriate
        // host. Horizontal => inside the custom titlebar (alongside a
        // transparent drag handle that fills the trailing space);
        // Vertical => docked Left in the content area, with the drag
        // handle alone occupying the title-bar slot so the user can still
        // grab the title bar to move the window.
        this.titleBarTabDock.Children.Clear();
        if (vertical)
        {
            this.sideTabHost.Child = this.tabStrip;
            DockPanel.SetDock(this.titleBarDragHandle, Dock.Left);
            this.titleBarTabDock.Children.Add(this.titleBarDragHandle);
            this.titleBarTabHost.Child = this.titleBarTabDock;
            this.titleBarTabHost.IsVisible = true;
            this.sideTabHost.IsVisible = true;
        }
        else
        {
            this.sideTabHost.Child = null;

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
            this.sideTabHost.IsVisible = false;
            this.titleBarTabHost.IsVisible = true;
        }

        // Update TopInset on all existing terminal controls to match
        // the current orientation. Horizontal mode: terminals render a
        // blurred preview in the top inset area behind the floating
        // title bar. Vertical mode: no inset, terminals start at the top.
        foreach (var tab in this.tabView.Tabs)
        {
            this.ApplyTopInsetToSession(tab);
        }

        this.UpdateTabStripVisibility();
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
        float inset = horizontal ? (float)TitleBarHeight : 0f;
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

    private void ApplyTabForegroundFromColorScheme()
    {
        var scheme = Models.ColorSchemePresets.FindByName(this.settings.ColorSchemeName)
            ?? Models.ColorSchemePresets.Default;
        this.tabStrip.ApplyForegroundColor(scheme.Foreground);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }
}
