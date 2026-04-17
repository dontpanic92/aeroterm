// <copyright file="TabViewTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;

/// <summary>
/// Pure-logic tests for <see cref="TabView"/> and <see cref="TabSession"/>.
/// Uses <see cref="FakeTabContent"/> to avoid spinning up a real PTY or
/// shell. Avalonia-headless <see cref="AvaloniaTestAttribute"/> is used
/// because constructing <see cref="TabView"/> touches Avalonia controls
/// internally.
/// </summary>
[TestFixture]
public class TabViewTests
{
    /// <summary>
    /// The first tab added to an empty view becomes active automatically.
    /// </summary>
    [AvaloniaTest]
    public void AddTab_FirstTab_BecomesActive()
    {
        var view = new TabView();
        var t = new TabSession(new FakeTabContent("a"));
        view.AddTab(t);
        Assert.That(view.ActiveTab, Is.SameAs(t));
    }

    /// <summary>
    /// Adding a second tab does not reactivate — the caller drives focus.
    /// </summary>
    [AvaloniaTest]
    public void AddTab_SecondTab_DoesNotReactivate()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        Assert.That(view.ActiveTab, Is.SameAs(a));
    }

    /// <summary>
    /// Closing the active middle tab of three activates the right neighbour.
    /// </summary>
    [AvaloniaTest]
    public void CloseTab_ActiveMiddleOfThree_ActivatesRightNeighbour()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);
        view.ActivateTab(b);
        view.CloseTab(b);
        Assert.That(view.ActiveTab, Is.SameAs(c));
        Assert.That(view.Tabs, Is.EqualTo(new[] { a, c }));
    }

    /// <summary>
    /// Closing the rightmost active tab activates the tab to its left.
    /// </summary>
    [AvaloniaTest]
    public void CloseTab_ActiveRightmost_ActivatesLeftNeighbour()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);
        view.ActivateTab(c);
        view.CloseTab(c);
        Assert.That(view.ActiveTab, Is.SameAs(b));
    }

    /// <summary>
    /// Closing the last remaining tab raises <see cref="TabView.LastTabClosed"/>.
    /// </summary>
    [AvaloniaTest]
    public void CloseTab_LastRemaining_RaisesLastTabClosed()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        view.AddTab(a);
        int raised = 0;
        view.LastTabClosed += () => raised++;
        view.CloseTab(a);
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(view.ActiveTab, Is.Null);
    }

    /// <summary>
    /// <see cref="TabView.ActivateByIndex"/> clamps out-of-range values to a no-op.
    /// </summary>
    [AvaloniaTest]
    public void ActivateByIndex_OutOfRange_IsNoOp()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        view.AddTab(a);
        view.ActivateByIndex(-1);
        view.ActivateByIndex(5);
        Assert.That(view.ActiveTab, Is.SameAs(a));
    }

    /// <summary>
    /// <see cref="TabView.ActivateNext"/> wraps past the last tab.
    /// </summary>
    [AvaloniaTest]
    public void ActivateNext_WrapsAround()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);
        view.ActivateTab(c);
        view.ActivateNext();
        Assert.That(view.ActiveTab, Is.SameAs(a));
    }

    /// <summary>
    /// <see cref="TabView.ActivatePrev"/> wraps past the first tab.
    /// </summary>
    [AvaloniaTest]
    public void ActivatePrev_WrapsAround()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        view.ActivateTab(a);
        view.ActivatePrev();
        Assert.That(view.ActiveTab, Is.SameAs(b));
    }

    /// <summary>
    /// Closing a tab disposes its underlying session content exactly once.
    /// </summary>
    [AvaloniaTest]
    public void CloseTab_DisposesClosedSessionCoordinator()
    {
        var view = new TabView();
        var fake = new FakeTabContent("a");
        var a = new TabSession(fake);
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        view.CloseTab(a);
        Assert.That(fake.DisposeCount, Is.EqualTo(1));
        Assert.That(a.IsDisposed, Is.True);
    }

    /// <summary>
    /// <see cref="TabSession.Dispose"/> forwards to content exactly once,
    /// even when called repeatedly.
    /// </summary>
    [AvaloniaTest]
    public void TabSession_Dispose_DisposesContentExactlyOnce()
    {
        var fake = new FakeTabContent("a");
        var t = new TabSession(fake);
        t.Dispose();
        t.Dispose();
        Assert.That(fake.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>
    /// <see cref="TabSession.TitleChanged"/> fires when the underlying
    /// content raises its title-change event, and <see cref="TabSession.Title"/>
    /// reflects the new value.
    /// </summary>
    [AvaloniaTest]
    public void TabSession_TitleChanged_FiresWhenContentRaisesIt()
    {
        var fake = new FakeTabContent("initial");
        var t = new TabSession(fake);
        string? latest = null;
        t.TitleChanged += s => latest = s;
        fake.RaiseTitle("updated");
        Assert.That(latest, Is.EqualTo("updated"));
        Assert.That(t.Title, Is.EqualTo("updated"));
    }
}
