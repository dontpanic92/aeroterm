// <copyright file="MainWindowHeadlessTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.UI;

using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroTerm;
using AeroTerm.Controls;
using AeroTerm.Services;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NUnit.Framework;

/// <summary>
/// Headless UI tests for <see cref="MainWindow"/>. Exercises tab creation /
/// close / switch shortcuts, the confirm-on-close flow, and the smoke-
/// test path of opening the window with a single tab.
/// <para>
/// Each test sets <see cref="App.TestTabContentFactory"/> so
/// <c>MainWindow.CreateTabSession</c> produces <see cref="FakeTabContent"/>
/// instead of spawning a real PTY child. The factory is cleared in
/// <see cref="TearDown"/> so no state leaks across tests.
/// </para>
/// </summary>
[TestFixture]
public class MainWindowHeadlessTests
{
    /// <summary>
    /// Installs the fake-tab-content factory before each test so windows do
    /// not spawn real shell processes.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        App.TestTabContentFactory = _ => new FakeTabContent("AeroTerm");
        App.TestConfirmCloseHandler = null;
    }

    /// <summary>
    /// Clears the test seams so they do not leak into the next test.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        App.TestTabContentFactory = null;
        App.TestConfirmCloseHandler = null;
    }

    /// <summary>
    /// Opening a fresh <see cref="MainWindow"/> populates a single active tab.
    /// </summary>
    [AvaloniaTest]
    public void MainWindow_OpensWithSingleTab()
    {
        var window = OpenWindow();

        Assert.That(GetTabView(window).Tabs, Has.Count.EqualTo(1));
        Assert.That(GetTabView(window).ActiveTab, Is.Not.Null);
    }

    /// <summary>
    /// Ctrl+Shift+T (non-macOS) / Cmd+T (macOS) creates a second tab and
    /// leaves it as the active tab.
    /// </summary>
    [AvaloniaTest]
    public void NewTab_Shortcut_AddsAndActivatesTab()
    {
        var window = OpenWindow();
        var tabs = GetTabView(window);

        if (IsMac())
        {
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Meta);
        }
        else
        {
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);
        }

        PumpJobs();
        Assert.That(tabs.Tabs, Has.Count.EqualTo(2));
        Assert.That(tabs.ActiveTab, Is.SameAs(tabs.Tabs[1]));
    }

    /// <summary>
    /// With two tabs open, the close-tab chord reduces the count to one.
    /// </summary>
    [AvaloniaTest]
    public void CloseTab_Shortcut_ClosesActiveTab()
    {
        var window = OpenWindow();
        var tabs = GetTabView(window);
        AddFakeTab(tabs);
        tabs.ActivateByIndex(1);

        if (IsMac())
        {
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Meta);
        }
        else
        {
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control | RawInputModifiers.Shift);
        }

        PumpJobs();
        Assert.That(tabs.Tabs, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Ctrl+PageDown moves to the next tab; Ctrl+PageUp moves back. Only
    /// runs on non-macOS where the chord is defined; on macOS the analogous
    /// chord is Ctrl+Tab / Ctrl+Shift+Tab.
    /// </summary>
    [AvaloniaTest]
    [Platform(Exclude = "MacOsX", Reason = "PageDown/PageUp tab chords are non-mac defaults.")]
    public void NextPrev_Tab_Shortcut_CyclesActive()
    {
        var window = OpenWindow();
        var tabs = GetTabView(window);
        AddFakeTab(tabs);
        AddFakeTab(tabs);
        tabs.ActivateByIndex(0);

        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.Control);
        PumpJobs();
        Assert.That(tabs.Tabs.IndexOf(tabs.ActiveTab!), Is.EqualTo(1));

        window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.Control);
        PumpJobs();
        Assert.That(tabs.Tabs.IndexOf(tabs.ActiveTab!), Is.EqualTo(0));
    }

    /// <summary>
    /// Ctrl+2 (or Cmd+2 on mac) activates the tab at zero-based index 1.
    /// </summary>
    [AvaloniaTest]
    public void JumpToTab_Shortcut_ActivatesByIndex()
    {
        var window = OpenWindow();
        var tabs = GetTabView(window);
        AddFakeTab(tabs);
        AddFakeTab(tabs);
        tabs.ActivateByIndex(0);

        var mods = IsMac() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        window.KeyPressQwerty(PhysicalKey.Digit2, mods);
        PumpJobs();

        Assert.That(tabs.ActiveTab, Is.SameAs(tabs.Tabs[1]));
    }

    /// <summary>
    /// With <see cref="AppSettings.ConfirmOnClose"/> enabled and more than
    /// one tab, attempting to close the window is deferred to the (stubbed)
    /// confirmation flow — the window stays open and the tabs survive.
    /// </summary>
    [AvaloniaTest]
    public void ConfirmOnClose_WhenMultipleTabsOpen_CancelsClose()
    {
        var settings = new AppSettings { ConfirmOnClose = true };
        App.TestConfirmCloseHandler = _ => Task.FromResult(false);

        var window = new MainWindow(settings);
        window.Show();
        PumpJobs();

        var tabs = GetTabView(window);
        AddFakeTab(tabs);
        Assume.That(tabs.Tabs, Has.Count.EqualTo(2));

        window.Close();
        PumpJobs();

        Assert.That(window.IsVisible, Is.True, "Window should not have closed when confirmation was declined.");
        Assert.That(tabs.Tabs, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// Splitting the active tab's pane via the public
    /// <see cref="TabSession.SplitActivePane"/> API grows the tree to
    /// two leaves and the session's visual is a
    /// <see cref="AeroTerm.Controls.Panes.PaneTreeView"/> that hosts a
    /// <see cref="GridSplitter"/> between two pane hosts.
    /// </summary>
    [AvaloniaTest]
    public void SplitActivePane_GrowsPaneTreeAndAddsSplitter()
    {
        var window = OpenWindow();
        var tabs = GetTabView(window);
        var session = tabs.ActiveTab!;
        Assert.That(session.PaneCount, Is.EqualTo(1));

        session.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Vertical);
        PumpJobs();

        Assert.That(session.PaneCount, Is.EqualTo(2));
        Assert.That(session.Control, Is.InstanceOf<AeroTerm.Controls.Panes.PaneTreeView>());
        var splitters = DescendantsOfType<GridSplitter>(session.Control).ToList();
        Assert.That(splitters, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Closing the last surviving pane of the only tab via
    /// <see cref="TabSession.CloseActivePane"/> returns
    /// <see langword="false"/>. The window-level handler would then
    /// remove the tab; the session reports the last-pane-closed state.
    /// </summary>
    [AvaloniaTest]
    public void CloseActivePane_LastPane_ReturnsFalse()
    {
        var window = OpenWindow();
        var session = GetTabView(window).ActiveTab!;

        bool alive = session.CloseActivePane();
        PumpJobs();

        Assert.That(alive, Is.False);
    }

    /// <summary>
    /// After a split, closing the active pane leaves the tab alive
    /// with a single pane and the splitter is gone.
    /// </summary>
    [AvaloniaTest]
    public void CloseActivePane_AfterSplit_CollapsesToSinglePane()
    {
        var window = OpenWindow();
        var session = GetTabView(window).ActiveTab!;
        session.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Horizontal);
        PumpJobs();
        Assert.That(session.PaneCount, Is.EqualTo(2));

        bool alive = session.CloseActivePane();
        PumpJobs();

        Assert.That(alive, Is.True);
        Assert.That(session.PaneCount, Is.EqualTo(1));
        Assert.That(DescendantsOfType<GridSplitter>(session.Control), Is.Empty);
    }

    private static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static MainWindow OpenWindow()
    {
        var window = new MainWindow(new AppSettings());
        window.Show();
        PumpJobs();
        return window;
    }

    private static TabView GetTabView(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "tabView",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new System.InvalidOperationException("tabView field missing.");
        return (TabView)field.GetValue(window)!;
    }

    private static void AddFakeTab(TabView tabs)
    {
        tabs.AddTab(new TabSession(new FakeTabContent("tab")));
    }

    private static void PumpJobs()
    {
        // Flush any pending dispatcher work so Show-driven initial tab
        // creation and keyboard dispatch complete before assertions.
        Dispatcher.UIThread.RunJobs();
    }

    private static System.Collections.Generic.IEnumerable<T> DescendantsOfType<T>(Control root)
        where T : Control
    {
        var stack = new System.Collections.Generic.Stack<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is T typed)
            {
                yield return typed;
            }

            if (current is Panel panel)
            {
                foreach (var c in panel.Children)
                {
                    if (c is Control cc)
                    {
                        stack.Push(cc);
                    }
                }
            }
            else if (current is ContentControl cc)
            {
                if (cc.Content is Control child)
                {
                    stack.Push(child);
                }
            }
            else if (current is Decorator dec)
            {
                if (dec.Child is Control child)
                {
                    stack.Push(child);
                }
            }
        }
    }
}
