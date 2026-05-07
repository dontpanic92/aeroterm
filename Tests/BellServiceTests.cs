// <copyright file="BellServiceTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="BellService"/> visual-bell state restoration.
/// </summary>
[TestFixture]
public class BellServiceTests
{
    /// <summary>
    /// A single visual bell restores the target border to the pre-flash state.
    /// </summary>
    [AvaloniaTest]
    public void Visual_SingleFlash_RestoresOriginalBorder()
    {
        var callbacks = new List<Action>();
        var originalBrush = new SolidColorBrush(Colors.Black);
        var originalThickness = new Thickness(3, 4, 5, 6);
        var border = CreateBorder(originalBrush, originalThickness);
        var (window, service) = CreateService(border, callbacks);

        try
        {
            service.Visual();

            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(2)));

            callbacks.Single().Invoke();

            Assert.That(border.BorderBrush, Is.SameAs(originalBrush));
            Assert.That(border.BorderThickness, Is.EqualTo(originalThickness));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Overlapping visual bells keep the original restore baseline instead of
    /// restoring the active flash brush/thickness as if they were the original.
    /// </summary>
    [AvaloniaTest]
    public void Visual_OverlappingFlashes_RestoresOriginalBorder()
    {
        var callbacks = new List<Action>();
        var originalBrush = new SolidColorBrush(Colors.Black);
        var originalThickness = new Thickness(3, 4, 5, 6);
        var border = CreateBorder(originalBrush, originalThickness);
        var (window, service) = CreateService(border, callbacks);

        try
        {
            service.Visual();

            service.Visual();

            Assert.That(callbacks, Has.Count.EqualTo(2));
            Assert.That(border.BorderBrush, Is.Not.SameAs(originalBrush));
            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(2)));

            callbacks[0].Invoke();

            Assert.That(border.BorderBrush, Is.Not.SameAs(originalBrush));
            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(2)));

            callbacks[1].Invoke();

            Assert.That(border.BorderBrush, Is.SameAs(originalBrush));
            Assert.That(border.BorderThickness, Is.EqualTo(originalThickness));
        }
        finally
        {
            window.Close();
        }
    }

    private static Border CreateBorder(IBrush brush, Thickness thickness)
    {
        return new Border
        {
            BorderBrush = brush,
            BorderThickness = thickness,
        };
    }

    private static (Window Window, BellService Service) CreateService(Border border, List<Action> callbacks)
    {
        var window = new Window
        {
            Content = border,
        };
        var service = new BellService(
            new AppSettings(),
            window,
            border,
            (callback, _) => callbacks.Add(callback));
        return (window, service);
    }
}
