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

    /// <summary>
    /// <see cref="TabSession.CurrentWorkingDirectoryChanged"/> follows the
    /// active content's current-directory event.
    /// </summary>
    [AvaloniaTest]
    public void TabSession_CurrentWorkingDirectoryChanged_FiresWhenActiveContentRaisesIt()
    {
        var fake = new FakeTabContent("initial");
        var t = new TabSession(fake);
        string? latest = null;
        t.CurrentWorkingDirectoryChanged += cwd => latest = cwd;

        fake.RaiseCurrentWorkingDirectory("/work");

        Assert.That(latest, Is.EqualTo("/work"));
        Assert.That(t.CurrentWorkingDirectory, Is.EqualTo("/work"));
    }

    /// <summary>
    /// <see cref="TabView.DuplicateTab"/> inserts the new tab immediately
    /// after the source tab, not at the end of the collection.
    /// </summary>
    [AvaloniaTest]
    public void DuplicateTab_InsertsImmediatelyAfterSource()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);

        var dup = view.DuplicateTab(b);

        Assert.That(view.Tabs.Count, Is.EqualTo(4));
        Assert.That(view.Tabs[0], Is.SameAs(a));
        Assert.That(view.Tabs[1], Is.SameAs(b));
        Assert.That(view.Tabs[2], Is.SameAs(dup));
        Assert.That(view.Tabs[3], Is.SameAs(c));
    }

    /// <summary>
    /// <see cref="TabView.DuplicateTab"/> activates the newly-inserted duplicate.
    /// </summary>
    [AvaloniaTest]
    public void DuplicateTab_ActivatesTheNewDuplicate()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        view.ActivateTab(a);

        var dup = view.DuplicateTab(a);

        Assert.That(view.ActiveTab, Is.SameAs(dup));
    }

    /// <summary>
    /// <see cref="TabView.DuplicateTab"/> throws <see cref="ArgumentException"/>
    /// when the source tab is not a member of <see cref="TabView.Tabs"/>.
    /// </summary>
    [AvaloniaTest]
    public void DuplicateTab_InvalidSource_ThrowsArgumentException()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var stranger = new TabSession(new FakeTabContent("x"));
        view.AddTab(a);

        Assert.Throws<ArgumentException>(() => view.DuplicateTab(stranger));
    }

    /// <summary>
    /// <see cref="TabView.MoveTab"/> reorders tabs and preserves the active tab.
    /// </summary>
    [AvaloniaTest]
    public void MoveTab_ChangesOrder_PreservesActive()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);
        view.ActivateTab(b);

        view.MoveTab(0, 2);

        Assert.That(view.Tabs[0], Is.SameAs(b));
        Assert.That(view.Tabs[1], Is.SameAs(c));
        Assert.That(view.Tabs[2], Is.SameAs(a));
        Assert.That(view.ActiveTab, Is.SameAs(b));
    }

    /// <summary>
    /// Regression: <see cref="TabView.MoveTab"/> must keep every tab's
    /// content control attached to the visual tree. A previous bug let
    /// the <c>Move</c> notification fall through the
    /// <c>OldItems</c> branch of the collection-changed handler,
    /// detaching the moved tab's control and leaving the tab visually
    /// empty after a drag-reorder.
    /// </summary>
    [AvaloniaTest]
    public void MoveTab_KeepsAllTabContentsAttached()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);

        view.MoveTab(0, 2);

        Assert.That(a.Control.Parent, Is.Not.Null, "moved tab content was detached");
        Assert.That(b.Control.Parent, Is.Not.Null);
        Assert.That(c.Control.Parent, Is.Not.Null);
        Assert.That(a.Control.Parent, Is.SameAs(b.Control.Parent));
        Assert.That(a.Control.Parent, Is.SameAs(c.Control.Parent));
    }

    /// <summary>
    /// <see cref="TabView.MoveTab"/> is a no-op when indices are equal or out of range.
    /// </summary>
    [AvaloniaTest]
    public void MoveTab_OutOfRange_IsNoOp()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);

        view.MoveTab(0, 0);
        view.MoveTab(-1, 1);
        view.MoveTab(0, 5);

        Assert.That(view.Tabs[0], Is.SameAs(a));
        Assert.That(view.Tabs[1], Is.SameAs(b));
    }

    /// <summary>
    /// <see cref="TabView.MoveActiveTabLeft"/> / <see cref="TabView.MoveActiveTabRight"/> are
    /// no-ops at the edges.
    /// </summary>
    [AvaloniaTest]
    public void MoveActiveTab_AtEdges_IsNoOp()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        view.ActivateTab(a);

        view.MoveActiveTabLeft();
        Assert.That(view.Tabs[0], Is.SameAs(a));

        view.ActivateTab(b);
        view.MoveActiveTabRight();
        Assert.That(view.Tabs[1], Is.SameAs(b));
    }

    /// <summary>
    /// <see cref="TabView.MoveActiveTabRight"/> swaps the active tab with its right neighbour.
    /// </summary>
    [AvaloniaTest]
    public void MoveActiveTabRight_SwapsWithNeighbour()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        var c = new TabSession(new FakeTabContent("c"));
        view.AddTab(a);
        view.AddTab(b);
        view.AddTab(c);
        view.ActivateTab(a);

        view.MoveActiveTabRight();

        Assert.That(view.Tabs[0], Is.SameAs(b));
        Assert.That(view.Tabs[1], Is.SameAs(a));
        Assert.That(view.ActiveTab, Is.SameAs(a));
    }

    /// <summary>
    /// <see cref="TabView.DetachTab"/> removes a tab without disposing its content.
    /// </summary>
    [AvaloniaTest]
    public void DetachTab_RemovesWithoutDispose()
    {
        var view = new TabView();
        var fake = new FakeTabContent("a");
        var tab = new TabSession(fake);
        var other = new TabSession(new FakeTabContent("b"));
        view.AddTab(tab);
        view.AddTab(other);

        view.DetachTab(tab);

        Assert.That(view.Tabs, Does.Not.Contain(tab));
        Assert.That(fake.DisposeCount, Is.EqualTo(0));
        Assert.That(tab.IsDisposed, Is.False);
    }

    /// <summary>
    /// Detaching the single remaining tab does not raise <see cref="TabView.LastTabClosed"/>;
    /// it raises <see cref="TabView.TabDetached"/> instead so callers can orchestrate the move.
    /// </summary>
    [AvaloniaTest]
    public void DetachTab_LastTab_DoesNotFireLastTabClosed()
    {
        var view = new TabView();
        var tab = new TabSession(new FakeTabContent("a"));
        view.AddTab(tab);

        int lastClosed = 0;
        int detached = 0;
        view.LastTabClosed += () => lastClosed++;
        view.TabDetached += t =>
        {
            if (ReferenceEquals(t, tab))
            {
                detached++;
            }
        };

        view.DetachTab(tab);

        Assert.That(lastClosed, Is.EqualTo(0));
        Assert.That(detached, Is.EqualTo(1));
        Assert.That(view.Tabs, Is.Empty);
    }

    /// <summary>
    /// Detaching the active tab activates a neighbour (mirrors <see cref="TabView.CloseTab"/>).
    /// </summary>
    [AvaloniaTest]
    public void DetachTab_ActivatesNeighbourWhenActiveRemoved()
    {
        var view = new TabView();
        var a = new TabSession(new FakeTabContent("a"));
        var b = new TabSession(new FakeTabContent("b"));
        view.AddTab(a);
        view.AddTab(b);
        view.ActivateTab(a);

        view.DetachTab(a);

        Assert.That(view.ActiveTab, Is.SameAs(b));
    }
}
