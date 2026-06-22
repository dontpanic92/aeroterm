// <copyright file="CoordinatorTabContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.ComponentModel;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Production <see cref="ITabSessionContent"/> that wraps a
/// <see cref="TerminalSessionCoordinator"/> and hosts its
/// <see cref="TerminalControl"/> (plus the search overlay) inside a
/// <see cref="Grid"/> that stays attached to the visual tree for the
/// tab's lifetime.
/// </summary>
internal sealed class CoordinatorTabContent : ITabSessionContent
{
    private readonly TerminalSessionCoordinator coordinator;
    private readonly AppSettings? settings;
    private readonly Grid host = new();
    private readonly Border workbenchButtonStrip;
    private readonly Button terminalViewButton;
    private readonly Button gitViewButton;
    private readonly GitDiffPane gitPaneView;
    private readonly IBrush activeButtonBrush;
    private TerminalControl? terminal;
    private string title = "AeroTerm";
    private bool disposed;
    private bool started;
    private bool showingGitPane;
    private float lastTopInset;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoordinatorTabContent"/> class.
    /// </summary>
    /// <param name="coordinator">The coordinator this content wraps. Ownership
    /// transfers — <see cref="Dispose"/> will shut it down.</param>
    public CoordinatorTabContent(TerminalSessionCoordinator coordinator)
        : this(coordinator, settings: null)
    {
    }

    private CoordinatorTabContent(TerminalSessionCoordinator coordinator, AppSettings? settings)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.settings = settings;
        this.coordinator.TerminalReady += this.OnTerminalReady;
        this.coordinator.TitleChanged += this.OnCoordinatorTitleChanged;
        this.coordinator.ProcessExitedNormally += this.OnCoordinatorProcessExited;
        this.coordinator.CurrentWorkingDirectoryChanged += this.OnCoordinatorCurrentWorkingDirectoryChanged;

        this.activeButtonBrush = ResolveActiveButtonBrush(this.host);
        this.gitPaneView = new GitDiffPane(() => this.coordinator.TryGetCurrentWorkingDirectory());
        this.gitPaneView.IsVisible = false;
        this.gitPaneView.ZIndex = 1;
        this.host.Children.Add(this.gitPaneView);

        this.terminalViewButton = this.BuildViewButton(BuildTerminalIcon(), "Terminal view", this.ShowTerminalView);
        this.gitViewButton = this.BuildViewButton(BuildGitIcon(), "Git view", this.ShowGitView);
        this.workbenchButtonStrip = this.BuildWorkbenchButtonStrip();
        this.workbenchButtonStrip.ZIndex = 100;
        this.host.Children.Add(this.workbenchButtonStrip);

        if (this.settings is not null)
        {
            this.settings.PropertyChanged += this.OnSettingsPropertyChanged;
        }

        this.UpdateWorkbenchButtonsVisibility();
        this.UpdateActiveViewVisuals();
    }

    /// <inheritdoc />
    public event Action<string>? TitleChanged;

    /// <inheritdoc />
    public event Action? ProcessExitedNormally;

    /// <inheritdoc />
    public event Action<string>? CurrentWorkingDirectoryChanged;

    /// <inheritdoc />
    public string Title => this.title;

    /// <inheritdoc />
    public Control Host => this.host;

    /// <inheritdoc />
    public TerminalSessionCoordinator? Coordinator => this.coordinator;

    /// <inheritdoc />
    public TerminalControl? Terminal => this.terminal;

    /// <inheritdoc />
    public string? CurrentWorkingDirectory => this.coordinator.TryGetCurrentWorkingDirectory();

    /// <inheritdoc />
    public void Start()
    {
        if (this.started || this.disposed)
        {
            return;
        }

        this.started = true;
        this.coordinator.Initialize();
    }

    /// <inheritdoc />
    public void FocusInput() => this.terminal?.Focus();

    /// <inheritdoc />
    public ITabSessionContent Duplicate()
    {
        if (this.settings is null)
        {
            throw new InvalidOperationException(
                "This CoordinatorTabContent was not constructed with an AppSettings reference and cannot be duplicated. " +
                "Use the AppSettings-aware factory to enable duplication.");
        }

        // Build a spec from the source coordinator: same command / args / env
        // snapshot as the source at launch; live cwd if available, else the
        // source's launch cwd.
        var sourceSpec = this.coordinator.LastLaunchSpec;
        LaunchSpec? dupSpec = null;
        if (sourceSpec is not null)
        {
            string cwd = this.coordinator.TryGetCurrentWorkingDirectory() ?? sourceSpec.Cwd;
            dupSpec = sourceSpec.WithCwd(cwd);
        }

        var newCoord = new TerminalSessionCoordinator(this.settings, dupSpec);
        return FromCoordinator(newCoord, this.settings);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.coordinator.TerminalReady -= this.OnTerminalReady;
        this.coordinator.TitleChanged -= this.OnCoordinatorTitleChanged;
        this.coordinator.ProcessExitedNormally -= this.OnCoordinatorProcessExited;
        this.coordinator.CurrentWorkingDirectoryChanged -= this.OnCoordinatorCurrentWorkingDirectoryChanged;
        if (this.settings is not null)
        {
            this.settings.PropertyChanged -= this.OnSettingsPropertyChanged;
        }

        if (this.terminal is not null)
        {
            this.terminal.TopInsetChanged -= this.OnTerminalTopInsetChanged;
        }

        this.coordinator.Shutdown();
    }

    /// <summary>
    /// Factory that constructs a <see cref="CoordinatorTabContent"/> tied to
    /// <paramref name="settings"/> so it can later spawn duplicates via
    /// <see cref="Duplicate"/>.
    /// </summary>
    /// <param name="coordinator">Coordinator to wrap (ownership transfers).</param>
    /// <param name="settings">Application settings used by duplicates.</param>
    /// <returns>A newly-constructed content adapter.</returns>
    internal static CoordinatorTabContent FromCoordinator(TerminalSessionCoordinator coordinator, AppSettings settings)
    {
        return new CoordinatorTabContent(coordinator, settings);
    }

    private static IBrush ResolveActiveButtonBrush(Control reference)
    {
        if (reference.TryGetResource("TabStripActiveAccentBrush", reference.ActualThemeVariant, out var value))
        {
            if (value is IBrush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xA3, 0xFF));
    }

    private static PathIcon BuildTerminalIcon()
    {
        // A terminal window: outer frame with a small '>' prompt glyph.
        return new PathIcon
        {
            Width = 12,
            Height = 12,
            Data = Geometry.Parse(
                "M128,192 H896 V832 H128 Z M128,320 H896 M256,448 L384,576 256,704 M512,704 H704"),
        };
    }

    private static PathIcon BuildGitIcon()
    {
        // A simple branch glyph: a vertical line with a branch splitting off
        // to a node, evoking source-control branching.
        return new PathIcon
        {
            Width = 12,
            Height = 12,
            Data = Geometry.Parse(
                "M320,192 a96,96 0 1,0 0.1,0 Z M320,288 V736 M320,832 a96,96 0 1,0 0.1,0 Z " +
                "M704,256 a96,96 0 1,0 0.1,0 Z M704,352 V480 a128,128 0 0,1 -128,128 H320"),
        };
    }

    private void OnTerminalReady(TerminalControl control)
    {
        this.terminal = control;
        this.host.Children.Add(control);
        this.host.Children.Add(control.SearchOverlayVisual);
        this.SyncSearchOverlayMargin(control.TopInset);
        this.UpdateButtonStripMargin(control.TopInset);
        control.TopInsetChanged += this.OnTerminalTopInsetChanged;
        this.ApplyActiveViewVisibility();
    }

    private void OnTerminalTopInsetChanged(object? sender, float topInset)
    {
        this.SyncSearchOverlayMargin(topInset);
        this.UpdateButtonStripMargin(topInset);
    }

    private void SyncSearchOverlayMargin(float topInset)
    {
        if (this.terminal is null)
        {
            return;
        }

        // Anchor the overlay below the floating custom title bar so its
        // TextBox and buttons aren't z-occluded by the TitleBar grid (which
        // also lives at the top of the window and intercepts pointer hits
        // in the same band). The 8 / 12 px insets match the original
        // SearchOverlay.axaml margins for the visible top/right gap.
        this.terminal.SearchOverlayVisual.Margin = new Thickness(0, topInset + 8, 12, 0);
    }

    private void OnCoordinatorTitleChanged(string newTitle)
    {
        var t = string.IsNullOrEmpty(newTitle) ? "AeroTerm" : newTitle;
        if (this.title == t)
        {
            return;
        }

        this.title = t;
        this.TitleChanged?.Invoke(t);
    }

    private void OnCoordinatorProcessExited()
    {
        this.ProcessExitedNormally?.Invoke();
    }

    private void OnCoordinatorCurrentWorkingDirectoryChanged(string cwd)
    {
        if (this.showingGitPane)
        {
            _ = this.gitPaneView.RefreshAsync();
        }

        this.CurrentWorkingDirectoryChanged?.Invoke(cwd);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.EnableWorkbench))
        {
            if (this.settings is { EnableWorkbench: false })
            {
                // Snap back to the terminal so a hidden Git pane can't strand
                // the live session off-screen when the feature is turned off.
                this.showingGitPane = false;
            }

            this.UpdateWorkbenchButtonsVisibility();
            this.ApplyActiveViewVisibility();
            this.UpdateActiveViewVisuals();
        }
    }

    private void ShowTerminalView()
    {
        if (!this.showingGitPane)
        {
            return;
        }

        this.showingGitPane = false;
        this.ApplyActiveViewVisibility();
        this.UpdateActiveViewVisuals();
        this.terminal?.Focus();
    }

    private void ShowGitView()
    {
        if (this.showingGitPane)
        {
            return;
        }

        this.showingGitPane = true;
        this.ApplyActiveViewVisibility();
        this.UpdateActiveViewVisuals();
        _ = this.gitPaneView.RefreshAsync();
    }

    private void ApplyActiveViewVisibility()
    {
        // The terminal keeps running underneath; we only toggle visibility so
        // the PTY is never torn down when the Git pane is shown.
        if (this.terminal is not null)
        {
            this.terminal.IsVisible = !this.showingGitPane;

            // Only force the find overlay closed while the Git pane is shown.
            // When the terminal is visible we leave the overlay's own
            // open/closed state alone so it isn't spuriously revealed.
            if (this.showingGitPane)
            {
                this.terminal.SearchOverlayVisual.IsVisible = false;
            }
        }

        this.gitPaneView.IsVisible = this.showingGitPane;
    }

    private void UpdateWorkbenchButtonsVisibility()
    {
        this.workbenchButtonStrip.IsVisible = this.settings?.EnableWorkbench == true;
    }

    private void UpdateActiveViewVisuals()
    {
        this.terminalViewButton.Background = this.showingGitPane ? Brushes.Transparent : this.activeButtonBrush;
        this.gitViewButton.Background = this.showingGitPane ? this.activeButtonBrush : Brushes.Transparent;
    }

    private void UpdateButtonStripMargin(float topInset)
    {
        this.lastTopInset = topInset;

        // Sit just below the floating title bar inset, hugging the right edge.
        // Offset further left than the search overlay (12 px) so the two don't
        // stack directly on top of each other when search is open.
        this.workbenchButtonStrip.Margin = new Thickness(0, topInset + 8, 8, 0);

        // Keep the Git pane below the floating title bar so it doesn't cover it.
        this.gitPaneView.Margin = new Thickness(0, topInset, 0, 0);
    }

    private Border BuildWorkbenchButtonStrip()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { this.terminalViewButton, this.gitViewButton },
        };

        var strip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, this.lastTopInset + 8, 8, 0),
            Opacity = 0.45,
            Child = panel,
        };

        // Elegant, see-through by default; fully opaque while hovered.
        strip.PointerEntered += (_, _) => strip.Opacity = 1.0;
        strip.PointerExited += (_, _) => strip.Opacity = 0.45;
        return strip;
    }

    private Button BuildViewButton(PathIcon icon, string accessibleName, Action onClick)
    {
        var button = new Button
        {
            Content = icon,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(4),
            MinWidth = 0,
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, accessibleName);
        button.Click += (_, _) => onClick();
        return button;
    }
}
