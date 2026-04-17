// <copyright file="QuakeWindow.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using System;
using AeroTerm.Controls;
using AeroTerm.Diagnostics;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Borderless, topmost, taskbar-hidden drop-down terminal window anchored
/// to the top edge of the active monitor. Engaged by a global hotkey
/// managed by <see cref="QuakeModeService"/>. The window slides down on
/// show and slides back up + hides on dismiss; the single instance
/// persists for the process lifetime so sessions survive between toggles.
/// </summary>
public partial class QuakeWindow : Window
{
    private const double HeightFraction = 0.40;
    private const int AnimationMs = 150;
    private const int AnimationSteps = 10;

    private readonly AppSettings settings;
    private readonly TabView tabView;
    private readonly Border terminalBorder;
    private readonly ILogger log;
    private DispatcherTimer? animationTimer;
    private double animationTarget;
    private double animationOrigin;
    private int animationStep;
    private bool isHiding;
    private bool hasSpawnedInitialTab;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuakeWindow"/> class.
    /// Parameterless constructor used by the Avalonia XAML loader and the
    /// designer; production code should call
    /// <see cref="QuakeWindow(AppSettings)"/>.
    /// </summary>
    public QuakeWindow()
        : this(AppSettings.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuakeWindow"/> class.
    /// </summary>
    /// <param name="settings">Application settings driving the default profile selection and appearance.</param>
    public QuakeWindow(AppSettings settings)
    {
        this.settings = settings;
        this.log = AppLogger.For<QuakeWindow>();
        this.InitializeComponent();

        this.terminalBorder = this.FindControl<Border>("TerminalBorder")!;
        this.tabView = new TabView();
        this.tabView.LastTabClosed += this.OnLastTabClosed;
        this.terminalBorder.Child = this.tabView;

        this.Deactivated += (_, _) => this.HideWithAnimation();
        this.Opened += (_, _) => this.EnsureInitialTab();
    }

    /// <summary>
    /// Shows the window (if hidden), positions it on the active monitor,
    /// animates it in, and gives focus to the active tab's terminal. If
    /// already visible, hides it with the exit animation instead.
    /// </summary>
    public void Toggle()
    {
        if (this.IsVisible && !this.isHiding)
        {
            this.HideWithAnimation();
            return;
        }

        this.ShowWithAnimation();
    }

    /// <summary>
    /// Shows the window on the appropriate monitor with the slide-down animation.
    /// </summary>
    public void ShowWithAnimation()
    {
        this.isHiding = false;
        this.PositionOnActiveScreen(out double shownTop, out double hiddenTop);
        this.Position = new PixelPoint(this.Position.X, (int)hiddenTop);

        if (!this.IsVisible)
        {
            this.Show();
        }

        this.Activate();
        this.EnsureInitialTab();

        this.animationOrigin = hiddenTop;
        this.animationTarget = shownTop;
        this.StartAnimation(onComplete: () =>
        {
            if (this.tabView.ActiveTab is { } active)
            {
                active.FocusInput();
            }
        });
    }

    /// <summary>
    /// Animates the window up and off-screen, then hides it. The window
    /// instance and its tab sessions are preserved.
    /// </summary>
    public void HideWithAnimation()
    {
        if (!this.IsVisible || this.isHiding)
        {
            return;
        }

        this.isHiding = true;
        this.PositionOnActiveScreen(out double shownTop, out double hiddenTop);
        this.animationOrigin = this.Position.Y;
        this.animationTarget = hiddenTop;
        _ = shownTop;
        this.StartAnimation(onComplete: () =>
        {
            this.Hide();
            this.isHiding = false;
        });
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void PositionOnActiveScreen(out double shownTop, out double hiddenTop)
    {
        Screen? screen = null;
        try
        {
            screen = this.Screens.ScreenFromWindow(this) ?? this.Screens.Primary;
        }
        catch (Exception)
        {
            screen = null;
        }

        screen ??= this.Screens.Primary;
        shownTop = 0;
        hiddenTop = -200;

        if (screen is null)
        {
            this.Width = 800;
            this.Height = 400;
            this.Position = new PixelPoint(0, (int)hiddenTop);
            return;
        }

        var work = screen.WorkingArea;
        double scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        double logicalWidth = work.Width / scale;
        double logicalHeight = (work.Height * HeightFraction) / scale;

        this.Width = logicalWidth;
        this.Height = logicalHeight;
        shownTop = work.Y;
        hiddenTop = work.Y - (work.Height * HeightFraction) - 8;
        this.Position = new PixelPoint(work.X, (int)shownTop);
    }

    private void StartAnimation(Action onComplete)
    {
        this.animationTimer?.Stop();
        this.animationStep = 0;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, AnimationMs / AnimationSteps)),
        };
        timer.Tick += (_, _) =>
        {
            this.animationStep++;
            double t = Math.Min(1.0, (double)this.animationStep / AnimationSteps);

            // Ease-out cubic.
            double eased = 1 - Math.Pow(1 - t, 3);
            double y = this.animationOrigin + ((this.animationTarget - this.animationOrigin) * eased);
            this.Position = new PixelPoint(this.Position.X, (int)y);
            if (t >= 1.0)
            {
                timer.Stop();
                this.animationTimer = null;
                onComplete();
            }
        };
        this.animationTimer = timer;
        timer.Start();
    }

    private void EnsureInitialTab()
    {
        if (this.hasSpawnedInitialTab || this.tabView.Tabs.Count > 0)
        {
            this.hasSpawnedInitialTab = true;
            return;
        }

        this.hasSpawnedInitialTab = true;
        try
        {
            TabSession session;
            var factory = App.TestTabContentFactory;
            if (factory is not null)
            {
                session = new TabSession(factory(this.settings));
            }
            else
            {
                var profile = App.Profiles.DefaultProfile ?? ProfileStore.CreateSynthesizedDefault();
                session = new TabSession(this.settings, profile, fallback: null);
            }

            this.tabView.AddTab(session);
            this.tabView.ActivateTab(session);
            Dispatcher.UIThread.RunJobs();
            session.Start();
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Failed to spawn Quake window initial tab.");
        }
    }

    private void OnLastTabClosed()
    {
        // Preserve the quake instance: hide instead of closing, and allow
        // a fresh initial tab to spawn the next time the hotkey is pressed.
        this.hasSpawnedInitialTab = false;
        this.HideWithAnimation();
    }
}
