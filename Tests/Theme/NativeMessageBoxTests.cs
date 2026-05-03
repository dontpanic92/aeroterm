// <copyright file="NativeMessageBoxTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System.Linq;
using AeroTerm.Theme.Controls;
using AeroTerm.Theme.NativeMessageBoxes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using NUnit.Framework;

/// <summary>
/// Tests for the native message-box model and Avalonia fallback.
/// </summary>
[TestFixture]
public class NativeMessageBoxTests
{
    /// <summary>
    /// OK-only options use a single OK result and default label.
    /// </summary>
    [Test]
    public void CreateOk_UsesOkDefaults()
    {
        NativeMessageBoxOptions options = NativeMessageBoxOptions.CreateOk("Title", "Message", okText: null);

        Assert.Multiple(() =>
        {
            Assert.That(options.Buttons, Is.EqualTo(NativeMessageBoxButtons.Ok));
            Assert.That(options.PrimaryButtonText, Is.EqualTo("OK"));
            Assert.That(options.SecondaryButtonText, Is.Null);
            Assert.That(options.CancelResult, Is.EqualTo(NativeMessageBoxResult.Ok));
        });
    }

    /// <summary>
    /// Yes/No options preserve custom localized button text.
    /// </summary>
    [Test]
    public void CreateYesNo_UsesCustomLabels()
    {
        NativeMessageBoxOptions options = NativeMessageBoxOptions.CreateYesNo(
            "Close window?",
            "Two tabs are open.",
            "Close",
            "Cancel");

        Assert.Multiple(() =>
        {
            Assert.That(options.Buttons, Is.EqualTo(NativeMessageBoxButtons.YesNo));
            Assert.That(options.PrimaryButtonText, Is.EqualTo("Close"));
            Assert.That(options.SecondaryButtonText, Is.EqualTo("Cancel"));
            Assert.That(options.CancelResult, Is.EqualTo(NativeMessageBoxResult.No));
        });
    }

    /// <summary>
    /// The Avalonia fallback builds one default/cancel OK button.
    /// </summary>
    [AvaloniaTest]
    public void AvaloniaFallback_OkWindowCreatesOneButton()
    {
        NativeMessageBoxOptions options = NativeMessageBoxOptions.CreateOk("Title", "Message", okText: null);

        AvaloniaMessageBoxWindow window = AvaloniaNativeMessageBoxAdapter.CreateWindow(options);

        Assert.Multiple(() =>
        {
            Assert.That(window.Title, Is.EqualTo("Title"));
            Assert.That(window.Classes.Contains("dialog"), Is.True);
            Assert.That(window.ActionButtons, Has.Count.EqualTo(1));
            Assert.That(window.ActionButtons[0].Content, Is.EqualTo("OK"));
            Assert.That(window.ActionButtons[0].IsDefault, Is.True);
            Assert.That(window.ActionButtons[0].IsCancel, Is.True);
            Assert.That(window.CurrentResult, Is.EqualTo(NativeMessageBoxResult.Ok));
        });
    }

    /// <summary>
    /// The Avalonia fallback builds Yes and No buttons with No as the safe default.
    /// </summary>
    [AvaloniaTest]
    public void AvaloniaFallback_YesNoWindowCreatesSafeButtons()
    {
        NativeMessageBoxOptions options = NativeMessageBoxOptions.CreateYesNo(
            "Title",
            "Message",
            "Close",
            "Cancel");

        AvaloniaMessageBoxWindow window = AvaloniaNativeMessageBoxAdapter.CreateWindow(options);
        var buttons = window.ActionButtons.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(buttons, Has.Length.EqualTo(2));
            Assert.That(buttons[0].Content, Is.EqualTo("Close"));
            Assert.That(buttons[0].Classes.Contains("accent"), Is.True);
            Assert.That(buttons[0].IsDefault, Is.False);
            Assert.That(buttons[1].Content, Is.EqualTo("Cancel"));
            Assert.That(buttons[1].IsDefault, Is.True);
            Assert.That(buttons[1].IsCancel, Is.True);
            Assert.That(window.CurrentResult, Is.EqualTo(NativeMessageBoxResult.No));
        });
    }

    /// <summary>
    /// Clicking the Yes button records the Yes result.
    /// </summary>
    [AvaloniaTest]
    public void AvaloniaFallback_ClickingYesSetsResult()
    {
        NativeMessageBoxOptions options = NativeMessageBoxOptions.CreateYesNo(
            "Title",
            "Message",
            "Yes",
            "No");
        AvaloniaMessageBoxWindow window = AvaloniaNativeMessageBoxAdapter.CreateWindow(options);
        Button yesButton = window.ActionButtons[0];

        yesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(window.CurrentResult, Is.EqualTo(NativeMessageBoxResult.Yes));
    }
}
