// <copyright file="CoordinatorTabContentWorkbenchTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Linq;
using AeroTerm.Controls;
using AeroTerm.Services;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NUnit.Framework;

/// <summary>
/// Headless tests for the per-terminal Workbench view-switch buttons hosted
/// by <see cref="CoordinatorTabContent"/>.
/// </summary>
[TestFixture]
public sealed class CoordinatorTabContentWorkbenchTests
{
    /// <summary>
    /// The button strip is shown only while the Workbench experiment is
    /// enabled.
    /// </summary>
    [AvaloniaTest]
    public void ButtonStrip_VisibilityFollowsWorkbenchSetting()
    {
        var settings = new AppSettings { EnableWorkbench = true };
        using var content = CreateContent(settings);

        Assert.That(GetButtonStrip(content).IsVisible, Is.True);

        settings.EnableWorkbench = false;
        Assert.That(GetButtonStrip(content).IsVisible, Is.False);

        settings.EnableWorkbench = true;
        Assert.That(GetButtonStrip(content).IsVisible, Is.True);
    }

    /// <summary>
    /// Clicking the Git icon swaps to the Git pane and highlights the
    /// Git button; clicking the terminal icon swaps back.
    /// </summary>
    [AvaloniaTest]
    public void ClickingButtons_TogglesViewAndActiveHighlight()
    {
        var settings = new AppSettings { EnableWorkbench = true };
        using var content = CreateContent(settings);

        var gitPane = GetGitPane(content);
        var terminalButton = GetButton(content, "Terminal view");
        var gitButton = GetButton(content, "Git view");

        Assert.That(gitPane.IsVisible, Is.False);
        Assert.That(IsTransparent(gitButton.Background), Is.True);
        Assert.That(IsTransparent(terminalButton.Background), Is.False);

        Click(gitButton);

        Assert.That(gitPane.IsVisible, Is.True);
        Assert.That(IsTransparent(gitButton.Background), Is.False);
        Assert.That(IsTransparent(terminalButton.Background), Is.True);

        Click(terminalButton);

        Assert.That(gitPane.IsVisible, Is.False);
        Assert.That(IsTransparent(gitButton.Background), Is.True);
        Assert.That(IsTransparent(terminalButton.Background), Is.False);
    }

    /// <summary>
    /// Each terminal control keeps its own toggle state.
    /// </summary>
    [AvaloniaTest]
    public void ToggleState_IsIndependentPerContent()
    {
        var settings = new AppSettings { EnableWorkbench = true };
        using var first = CreateContent(settings);
        using var second = CreateContent(settings);

        Click(GetButton(first, "Git view"));

        Assert.That(GetGitPane(first).IsVisible, Is.True);
        Assert.That(GetGitPane(second).IsVisible, Is.False);
    }

    /// <summary>
    /// Disabling the Workbench while the Git pane is shown snaps the content
    /// back to the terminal view so the live session is not left off-screen.
    /// </summary>
    [AvaloniaTest]
    public void DisablingWorkbench_SnapsBackToTerminalView()
    {
        var settings = new AppSettings { EnableWorkbench = true };
        using var content = CreateContent(settings);

        Click(GetButton(content, "Git view"));
        Assert.That(GetGitPane(content).IsVisible, Is.True);

        settings.EnableWorkbench = false;
        Assert.That(GetGitPane(content).IsVisible, Is.False);
    }

    private static CoordinatorTabContent CreateContent(AppSettings settings)
    {
        var coordinator = new TerminalSessionCoordinator(settings);
        return CoordinatorTabContent.FromCoordinator(coordinator, settings);
    }

    private static Border GetButtonStrip(CoordinatorTabContent content)
    {
        return content.Host.GetLogicalDescendants()
            .OfType<Border>()
            .First(b => b.Child is StackPanel);
    }

    private static GitDiffPane GetGitPane(CoordinatorTabContent content)
    {
        return content.Host.GetLogicalDescendants()
            .OfType<GitDiffPane>()
            .First();
    }

    private static Button GetButton(CoordinatorTabContent content, string accessibleName)
    {
        return content.Host.GetLogicalDescendants()
            .OfType<Button>()
            .First(b => AutomationProperties.GetName(b) == accessibleName);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static bool IsTransparent(IBrush? brush)
    {
        return brush is ISolidColorBrush solid && solid.Color.A == 0;
    }
}
