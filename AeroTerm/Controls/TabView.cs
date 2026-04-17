// <copyright file="TabView.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Content host for <see cref="TabSession"/> instances. Does NOT render the
/// tab strip itself — the strip (<see cref="TabStrip"/>) is placed by the
/// hosting window wherever is platform-appropriate (e.g. inside the title
/// bar row on Windows / Linux, below the traffic-light reservation on
/// macOS).
/// <para>
/// All tab visuals are kept simultaneously attached to the content area
/// with <c>IsVisible</c> toggled — the "hidden but attached" strategy.
/// </para>
/// </summary>
public sealed class TabView : UserControl, INotifyPropertyChanged
{
    private readonly Grid contentArea = new();
    private TabSession? activeTab;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabView"/> class.
    /// </summary>
    public TabView()
    {
        this.Tabs = new ObservableCollection<TabSession>();
        this.Tabs.CollectionChanged += this.OnTabsCollectionChanged;
        this.Content = this.contentArea;
    }

    /// <inheritdoc />
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised whenever <see cref="ActiveTab"/> changes. Argument is the new
    /// active tab (or <c>null</c> if none).
    /// </summary>
    public event Action<TabSession?>? ActiveTabChanged;

    /// <summary>
    /// Raised when the last remaining tab has just been closed. The
    /// hosting window typically subscribes to close itself.
    /// </summary>
    public event Action? LastTabClosed;

    /// <summary>
    /// Gets the observable collection of tabs in display order.
    /// </summary>
    public ObservableCollection<TabSession> Tabs { get; }

    /// <summary>
    /// Gets the currently active tab, or <c>null</c> if there are no tabs.
    /// </summary>
    public TabSession? ActiveTab
    {
        get => this.activeTab;
        private set
        {
            if (ReferenceEquals(this.activeTab, value))
            {
                return;
            }

            this.activeTab = value;
            this.ApplyActiveVisibility();
            this.OnPropertyChanged();
            this.ActiveTabChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Adds a tab to the end of the collection. Does NOT change the active
    /// tab unless this is the first tab — callers control focus.
    /// </summary>
    /// <param name="tab">The tab to add.</param>
    public void AddTab(TabSession tab)
    {
        if (tab is null)
        {
            throw new ArgumentNullException(nameof(tab));
        }

        this.Tabs.Add(tab);
    }

    /// <summary>
    /// Closes a tab: removes it from the collection, disposes its underlying
    /// session, and activates a neighbour if the closed tab was active. When
    /// the closed tab was the last one, <see cref="LastTabClosed"/> fires.
    /// </summary>
    /// <param name="tab">The tab to close.</param>
    public void CloseTab(TabSession tab)
    {
        if (tab is null)
        {
            throw new ArgumentNullException(nameof(tab));
        }

        int index = this.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        bool wasActive = ReferenceEquals(this.activeTab, tab);
        this.Tabs.RemoveAt(index);
        tab.Dispose();

        if (this.Tabs.Count == 0)
        {
            this.ActiveTab = null;
            this.LastTabClosed?.Invoke();
            return;
        }

        if (wasActive)
        {
            int next = Math.Min(index, this.Tabs.Count - 1);
            this.ActivateByIndex(next);
        }
    }

    /// <summary>
    /// Makes <paramref name="tab"/> the active tab. No-op if the tab is
    /// not a member of <see cref="Tabs"/> or is already active.
    /// </summary>
    /// <param name="tab">The tab to activate.</param>
    public void ActivateTab(TabSession tab)
    {
        if (tab is null)
        {
            throw new ArgumentNullException(nameof(tab));
        }

        if (!this.Tabs.Contains(tab))
        {
            return;
        }

        this.ActiveTab = tab;
    }

    /// <summary>
    /// Activates the tab at <paramref name="index"/> in <see cref="Tabs"/>.
    /// Out-of-range indices are silently ignored.
    /// </summary>
    /// <param name="index">Zero-based index into <see cref="Tabs"/>.</param>
    public void ActivateByIndex(int index)
    {
        if (index < 0 || index >= this.Tabs.Count)
        {
            return;
        }

        this.ActiveTab = this.Tabs[index];
    }

    /// <summary>
    /// Activates the tab immediately to the right of the active one,
    /// wrapping to the first tab from the last.
    /// </summary>
    public void ActivateNext()
    {
        if (this.Tabs.Count == 0)
        {
            return;
        }

        int i = this.activeTab is null ? -1 : this.Tabs.IndexOf(this.activeTab);
        int next = (i + 1) % this.Tabs.Count;
        this.ActiveTab = this.Tabs[next];
    }

    /// <summary>
    /// Activates the tab immediately to the left of the active one,
    /// wrapping to the last tab from the first.
    /// </summary>
    public void ActivatePrev()
    {
        if (this.Tabs.Count == 0)
        {
            return;
        }

        int i = this.activeTab is null ? 0 : this.Tabs.IndexOf(this.activeTab);
        int prev = (i - 1 + this.Tabs.Count) % this.Tabs.Count;
        this.ActiveTab = this.Tabs[prev];
    }

    /// <summary>
    /// Duplicates <paramref name="source"/>: inserts a new, cloned
    /// <see cref="TabSession"/> immediately after <paramref name="source"/>
    /// in <see cref="Tabs"/> and activates it. The returned session is
    /// <see cref="TabSession.Duplicate"/>d from the source and has NOT been
    /// started yet — callers are responsible for calling
    /// <see cref="TabSession.Start"/> and <see cref="TabSession.FocusInput"/>
    /// after a layout pass.
    /// </summary>
    /// <param name="source">Tab to clone. Must currently be in <see cref="Tabs"/>.</param>
    /// <returns>The newly-inserted duplicate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not a member of <see cref="Tabs"/>.</exception>
    public TabSession DuplicateTab(TabSession source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        int sourceIndex = this.Tabs.IndexOf(source);
        if (sourceIndex < 0)
        {
            throw new ArgumentException("Source tab is not a member of this TabView.", nameof(source));
        }

        var dup = source.Duplicate();
        this.Tabs.Insert(sourceIndex + 1, dup);
        this.ActiveTab = dup;
        return dup;
    }

    /// <summary>
    /// Creates and adds a new <see cref="TabSession"/> from a
    /// <see cref="Profile"/>. The profile's launch fields merge with the
    /// caller-supplied <paramref name="fallback"/> (profile wins) and the
    /// profile's appearance fields (color scheme, font list, font size)
    /// override the application defaults on the resulting terminal
    /// control. The returned session has not yet been started — callers
    /// activate it, force a layout pass, then call
    /// <see cref="TabSession.Start"/>.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="profile">Profile to launch.</param>
    /// <param name="fallback">Optional baseline launch spec that fills
    /// any fields the profile does not override.</param>
    /// <returns>The newly-added session.</returns>
    internal TabSession AddTab(AppSettings settings, Profile profile, LaunchSpec? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);
        var session = new TabSession(settings, profile, fallback);
        this.Tabs.Add(session);
        return session;
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TabSession tab in e.NewItems)
            {
                if (!this.contentArea.Children.Contains(tab.Control))
                {
                    tab.Control.IsVisible = false;
                    this.contentArea.Children.Add(tab.Control);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TabSession tab in e.OldItems)
            {
                this.contentArea.Children.Remove(tab.Control);
            }
        }

        // First tab added to an empty view becomes active automatically;
        // further additions keep the current active tab (caller decides).
        if (this.activeTab is null && this.Tabs.Count > 0)
        {
            this.ActiveTab = this.Tabs[0];
        }
    }

    private void ApplyActiveVisibility()
    {
        foreach (var tab in this.Tabs)
        {
            tab.Control.IsVisible = ReferenceEquals(tab, this.activeTab);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
