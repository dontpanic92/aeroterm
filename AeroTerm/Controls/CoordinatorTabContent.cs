// <copyright file="CoordinatorTabContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Models;
using AeroTerm.Services;
using AeroTerm.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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
    private readonly Profile? profileOverrides;
    private readonly Grid host = new();
    private TerminalControl? terminal;
    private string title = "AeroTerm";
    private bool disposed;
    private bool started;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoordinatorTabContent"/> class.
    /// </summary>
    /// <param name="coordinator">The coordinator this content wraps. Ownership
    /// transfers — <see cref="Dispose"/> will shut it down.</param>
    public CoordinatorTabContent(TerminalSessionCoordinator coordinator)
        : this(coordinator, settings: null, profileOverrides: null)
    {
    }

    private CoordinatorTabContent(TerminalSessionCoordinator coordinator, AppSettings? settings, Profile? profileOverrides)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.settings = settings;
        this.profileOverrides = profileOverrides;
        this.coordinator.TerminalReady += this.OnTerminalReady;
        this.coordinator.TitleChanged += this.OnCoordinatorTitleChanged;
        this.coordinator.ProcessExitedNormally += this.OnCoordinatorProcessExited;
    }

    /// <inheritdoc />
    public event Action<string>? TitleChanged;

    /// <inheritdoc />
    public event Action? ProcessExitedNormally;

    /// <inheritdoc />
    public string Title => this.title;

    /// <inheritdoc />
    public Control Host => this.host;

    /// <inheritdoc />
    public TerminalSessionCoordinator? Coordinator => this.coordinator;

    /// <inheritdoc />
    public TerminalControl? Terminal => this.terminal;

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
        return new CoordinatorTabContent(coordinator, settings, profileOverrides: null);
    }

    /// <summary>
    /// Factory that constructs a <see cref="CoordinatorTabContent"/> wired
    /// with profile appearance overrides. The overrides are applied once
    /// the underlying <see cref="TerminalControl"/> has been created (see
    /// <see cref="OnTerminalReady"/>).
    /// </summary>
    /// <param name="coordinator">Coordinator to wrap (ownership transfers).</param>
    /// <param name="settings">Application settings used by duplicates.</param>
    /// <param name="profile">Profile whose appearance fields should override
    /// the application defaults on the newly-created control.</param>
    /// <returns>A newly-constructed content adapter.</returns>
    internal static CoordinatorTabContent FromCoordinatorWithProfile(
        TerminalSessionCoordinator coordinator,
        AppSettings settings,
        Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new CoordinatorTabContent(coordinator, settings, profile);
    }

    private void OnTerminalReady(TerminalControl control)
    {
        this.terminal = control;
        this.host.Children.Add(control);
        this.host.Children.Add(control.SearchOverlayVisual);
        this.SyncSearchOverlayMargin(control.TopInset);
        control.TopInsetChanged += this.OnTerminalTopInsetChanged;
        this.ApplyProfileAppearanceOverrides(control);
    }

    private void OnTerminalTopInsetChanged(object? sender, float topInset)
    {
        this.SyncSearchOverlayMargin(topInset);
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

    private void ApplyProfileAppearanceOverrides(TerminalControl control)
    {
        if (this.profileOverrides is null || this.settings is null)
        {
            return;
        }

        // Color scheme override.
        if (!string.IsNullOrWhiteSpace(this.profileOverrides.ColorSchemeName))
        {
            var scheme = ColorSchemePresets.FindByName(this.profileOverrides.ColorSchemeName);
            if (scheme is not null)
            {
                Dispatcher.UIThread.Post(() => control.ApplyColorScheme(scheme));
            }
        }

        // Font priority / size override: build the priority list as the
        // coordinator's ApplyFontSettings does but sourcing from the profile.
        bool hasFontOverride = this.profileOverrides.FontFamilies is { Length: > 0 };
        bool hasSizeOverride = this.profileOverrides.FontSize is { } size && size > 0;
        if (hasFontOverride || hasSizeOverride)
        {
            var fontList = hasFontOverride
                ? new List<string>(this.profileOverrides.FontFamilies!)
                : new List<string>(this.settings.FallbackFonts);
            if (!hasFontOverride && !string.IsNullOrWhiteSpace(this.settings.FontFamily))
            {
                fontList.Insert(0, this.settings.FontFamily);
            }

            var normalized = FontPriorityList.Normalize(fontList);
            var expanded = FontPriorityList.Expand(normalized);
            double sizeToUse = hasSizeOverride ? this.profileOverrides.FontSize!.Value : this.settings.FontSize;
            Dispatcher.UIThread.Post(() => control.ApplyFontChange(expanded, sizeToUse));
        }
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
}
