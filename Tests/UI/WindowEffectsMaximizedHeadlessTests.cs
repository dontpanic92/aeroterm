// <copyright file="WindowEffectsMaximizedHeadlessTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.UI;

using System.ComponentModel;
using AeroTerm.WindowEffects;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

/// <summary>
/// Verifies that transparency effects collapse while the window is maximized.
/// </summary>
/// <remarks>
/// A translucent window stops the compositor from occlusion-culling the
/// windows behind it, so everything underneath keeps rendering and is blended
/// across the whole window on every screen refresh. The cost scales with the
/// window area, which makes a maximized translucent window the worst case.
/// The effective background alpha is the observable proxy for that state:
/// 255 means the window is fully opaque and the compositor can cull.
/// </remarks>
[TestFixture]
public class WindowEffectsMaximizedHeadlessTests
{
    /// <summary>
    /// Maximizing collapses effects to a fully opaque background when the
    /// option is enabled, and restores translucency on the way back.
    /// </summary>
    [AvaloniaTest]
    public void Maximized_WithOptionEnabled_CollapsesToOpaque()
    {
        var settings = new FakeEffectsSettings { DisableEffectsWhenMaximized = true };
        var window = new Window();
        var service = new WindowEffectsService(window, settings, NullLogger<WindowEffectsService>.Instance);

        byte alpha = 0;
        service.BackgroundAlphaChanged += value => alpha = value;

        service.HandleMacOSWindowStateChanged(WindowState.Normal);
        service.UpdateBackgroundOpacity();
        Assert.That(alpha, Is.LessThan(255), "A normal translucent window should stay non-opaque.");

        service.HandleMacOSWindowStateChanged(WindowState.Maximized);
        service.UpdateBackgroundOpacity();
        Assert.That(alpha, Is.EqualTo(255), "A maximized window should collapse effects to opaque.");

        service.HandleMacOSWindowStateChanged(WindowState.Normal);
        service.UpdateBackgroundOpacity();
        Assert.That(alpha, Is.LessThan(255), "Restoring the window should bring transparency back.");
    }

    /// <summary>
    /// With the option disabled, maximizing keeps the translucent appearance.
    /// </summary>
    [AvaloniaTest]
    public void Maximized_WithOptionDisabled_KeepsTransparency()
    {
        var settings = new FakeEffectsSettings { DisableEffectsWhenMaximized = false };
        var window = new Window();
        var service = new WindowEffectsService(window, settings, NullLogger<WindowEffectsService>.Instance);

        byte alpha = 255;
        service.BackgroundAlphaChanged += value => alpha = value;

        service.HandleMacOSWindowStateChanged(WindowState.Maximized);
        service.UpdateBackgroundOpacity();

        Assert.That(alpha, Is.LessThan(255));
    }

    /// <summary>
    /// Full screen collapses effects regardless of the maximized option.
    /// </summary>
    [AvaloniaTest]
    public void FullScreen_CollapsesToOpaque()
    {
        var settings = new FakeEffectsSettings { DisableEffectsWhenMaximized = false };
        var window = new Window();
        var service = new WindowEffectsService(window, settings, NullLogger<WindowEffectsService>.Instance);

        byte alpha = 0;
        service.BackgroundAlphaChanged += value => alpha = value;

        service.HandleMacOSWindowStateChanged(WindowState.FullScreen);
        service.UpdateBackgroundOpacity();

        Assert.That(alpha, Is.EqualTo(255));
    }

    private sealed class FakeEffectsSettings : IWindowEffectsSettings
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool EnableBlurBehind => true;

        public BlurType BlurType => BlurType.Acrylic;

        public bool DisableEffectsWhenMaximized { get; init; }

        public double BackgroundTintOpacity => 0.5;

        public double BackgroundMaterialOpacity => 0.5;

        public MaterialTone MaterialTone => MaterialTone.Dark;

        public void Raise(string name) =>
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
