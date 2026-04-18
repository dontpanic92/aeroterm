// <copyright file="TabStrip.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

/// <summary>
/// Horizontal tab strip for a <see cref="TabView"/>. Renders the active /
/// inactive tab buttons, each with a close (×) affordance, plus a trailing
/// "+" button that raises <see cref="NewTabRequested"/>. Middle-click on a
/// tab closes it. Colour / position transitions animate over 150 ms.
/// <para>
/// The strip is deliberately a separate control from <see cref="TabView"/>
/// so the hosting window can place it inside its title bar row on Windows /
/// Linux or below the traffic-light reservation on macOS.
/// </para>
/// </summary>
public sealed class TabStrip : UserControl
{
    /// <summary>
    /// Sentinel value passed via <see cref="TabGroupAssignmentRequested"/>
    /// when the user picks "New group…" from a tab's context menu. The
    /// subscriber is expected to create a fresh group via the app-level
    /// store and assign the tab to it.
    /// </summary>
    public const string CreateGroupSentinel = "__aeroterm_create_new_group__";

    private const double DragStartThreshold = 5.0;

    /// <summary>
    /// Horizontal rail width in vertical-orientation mode. Narrow enough
    /// to feel like a rail, wide enough to show a sensible title slice.
    /// </summary>
    private const double VerticalRailWidth = 180;

    private const byte TabForegroundAlpha = 0xF0;
    private const byte MutedForegroundAlpha = 0x90;
    private const byte InactiveTintAlpha = 0x10;
    private const byte InactiveHoverTintAlpha = 0x22;
    private const byte ActiveTintAlpha = 0x30;
    private const byte ActiveHoverTintAlpha = 0x45;

    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush CloseHoverBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush ActiveAccentBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xA3, 0xFF));

    // Instance brushes so the strip can recolor itself when the active
    // color scheme changes. Mutating SolidColorBrush.Color propagates to
    // every consumer automatically via Avalonia's property notification.
    // All four background tints are derived from the scheme's foreground
    // colour at varying alphas so the strip works on both dark and light
    // backgrounds (the scheme guarantees foreground contrasts with bg).
    private readonly SolidColorBrush tabForegroundBrush =
        new(Color.FromArgb(TabForegroundAlpha, 0xFF, 0xFF, 0xFF));

    private readonly SolidColorBrush mutedForegroundBrush =
        new(Color.FromArgb(MutedForegroundAlpha, 0xFF, 0xFF, 0xFF));

    private readonly SolidColorBrush inactiveTabBrush =
        new(Color.FromArgb(InactiveTintAlpha, 0xFF, 0xFF, 0xFF));

    private readonly SolidColorBrush inactiveHoverBrush =
        new(Color.FromArgb(InactiveHoverTintAlpha, 0xFF, 0xFF, 0xFF));

    private readonly SolidColorBrush activeTabBrush =
        new(Color.FromArgb(ActiveTintAlpha, 0xFF, 0xFF, 0xFF));

    private readonly SolidColorBrush activeHoverBrush =
        new(Color.FromArgb(ActiveHoverTintAlpha, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel tabsPanel;
    private readonly SplitButton newTabButton;
    private readonly MenuFlyout profileFlyout;
    private readonly Dictionary<TabSession, TabHeader> headers = new();
    private readonly Rectangle dropIndicator;
    private readonly StackPanel rootDock;
    private TabView? tabView;
    private IReadOnlyList<Profile> profiles = new List<Profile>();
    private TabGroupStore? groupStore;
    private DragState? drag;
    private Orientation orientation = Orientation.Horizontal;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabStrip"/> class.
    /// </summary>
    public TabStrip()
    {
        this.Focusable = false;
        this.tabsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        this.newTabButton = new SplitButton
        {
            Content = "+",
            Width = 48,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brushes.Transparent,
            Foreground = this.tabForegroundBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 16,
            Focusable = false,
        };
        this.profileFlyout = new MenuFlyout();
        this.newTabButton.Flyout = this.profileFlyout;
        AutomationProperties.SetName(this.newTabButton, "New tab");
        this.newTabButton.Click += (_, _) => this.NewTabRequested?.Invoke();
        this.RebuildProfileFlyout();

        this.dropIndicator = new Rectangle
        {
            Width = 3,
            Margin = new Thickness(1, 4, 1, 4),
            Fill = ActiveAccentBrush,
            IsVisible = false,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // StackPanel root keeps the tab-list panel and the trailing +
        // button in deterministic visual order regardless of DockPanel
        // measurement quirks when one of them is empty.
        this.rootDock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        this.rootDock.Children.Add(this.tabsPanel);
        this.rootDock.Children.Add(this.newTabButton);

        this.Content = this.rootDock;
        this.ApplyOrientation();

        // Drag handling — bubble so TabHeader still activates on press,
        // then we see the event and track movement for potential drag.
        // PointerReleased / CaptureLost subscribe with handledEventsToo so
        // child controls (terminal, buttons) marking the event Handled
        // don't strand the drag state and leave the drop indicator on
        // screen.
        this.AddHandler(PointerPressedEvent, this.OnStripPointerPressed, RoutingStrategies.Bubble);
        this.AddHandler(PointerMovedEvent, this.OnStripPointerMoved, RoutingStrategies.Bubble);
        this.AddHandler(PointerReleasedEvent, this.OnStripPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(PointerCaptureLostEvent, this.OnStripPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Raised when the user clicks the trailing "+" affordance.
    /// </summary>
    public event Action? NewTabRequested;

    /// <summary>
    /// Raised when the user picks a profile from the new-tab dropdown.
    /// </summary>
    public event Action<Profile>? NewTabWithProfileRequested;

    /// <summary>
    /// Raised when the user picks "Manage profiles…" from the new-tab dropdown.
    /// </summary>
    public event Action? ManageProfilesRequested;

    /// <summary>
    /// Raised when the user invokes "Duplicate tab" from a header's
    /// right-click context menu.
    /// </summary>
    public event Action<TabSession>? DuplicateTabRequested;

    /// <summary>
    /// Raised when the user completes a drag within the strip bounds that
    /// would reorder a tab. Arguments are source and target indices into
    /// <see cref="TabView.Tabs"/>. Subscribers typically call
    /// <see cref="TabView.MoveTab"/>.
    /// </summary>
    public event Action<int, int>? TabReorderRequested;

    /// <summary>
    /// Raised when the user drops a dragged tab outside the hosting
    /// window's bounds. Subscribers typically remove the tab from this
    /// view via <see cref="TabView.DetachTab"/> and re-parent it into a
    /// new window positioned near <c>screenPosition</c>.
    /// </summary>
    public event Action<TabSession, PixelPoint>? TabDetachRequested;

    /// <summary>
    /// Raised when the user picks an entry from a tab's "Add to group"
    /// submenu, "Remove from group", or "New group…". The group id is
    /// the target group's identifier, <c>null</c> to ungroup, or
    /// <see cref="CreateGroupSentinel"/> to request creation of a new
    /// group and assignment of the tab to it.
    /// </summary>
    public event Action<TabSession, string?>? TabGroupAssignmentRequested;

    /// <summary>
    /// Gets or sets the profile list populated into the "+" button's
    /// dropdown menu. Setting this rebuilds the flyout items. When
    /// <c>null</c> or empty, no dropdown arrow items are shown but the
    /// trailing "Manage profiles…" entry is still offered.
    /// </summary>
    public IReadOnlyList<Profile> Profiles
    {
        get => this.profiles;
        set
        {
            this.profiles = value ?? new List<Profile>();
            this.RebuildProfileFlyout();
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="TabGroupStore"/> backing the "Add to
    /// group" context-menu submenu and group-colored pills above each
    /// tab header. When <c>null</c>, tabs render without a pill and
    /// only offer "New group…"/"Remove from group" in the menu.
    /// </summary>
    public TabGroupStore? GroupStore
    {
        get => this.groupStore;
        set
        {
            if (ReferenceEquals(this.groupStore, value))
            {
                return;
            }

            if (this.groupStore is not null)
            {
                this.groupStore.GroupsChanged -= this.OnGroupsChanged;
            }

            this.groupStore = value;

            if (this.groupStore is not null)
            {
                this.groupStore.GroupsChanged += this.OnGroupsChanged;
            }

            this.OnGroupsChanged();
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="TabView"/> this strip renders. Setting
    /// this wires collection / active-tab change notifications.
    /// </summary>
    public TabView? View
    {
        get => this.tabView;
        set
        {
            if (ReferenceEquals(this.tabView, value))
            {
                return;
            }

            if (this.tabView is not null)
            {
                this.tabView.Tabs.CollectionChanged -= this.OnTabsChanged;
                this.tabView.ActiveTabChanged -= this.OnActiveTabChanged;
            }

            this.tabView = value;
            this.headers.Clear();
            this.tabsPanel.Children.Clear();

            if (this.tabView is not null)
            {
                this.tabView.Tabs.CollectionChanged += this.OnTabsChanged;
                this.tabView.ActiveTabChanged += this.OnActiveTabChanged;
                foreach (var t in this.tabView.Tabs)
                {
                    this.AddHeader(t);
                }

                this.UpdateStates();
            }
        }
    }

    /// <summary>
    /// Gets or sets the orientation of the tab strip. When set to
    /// <see cref="Avalonia.Layout.Orientation.Horizontal"/> (default) the
    /// strip renders as a classic left-to-right band; when set to
    /// <see cref="Avalonia.Layout.Orientation.Vertical"/> it becomes a
    /// narrow rail with tabs stacked top-to-bottom, the new-tab button at
    /// the top, and the active-tab accent moved to the leading edge.
    /// Setting this rebuilds the internal layout and all tab headers in
    /// place (the <see cref="TabView"/> binding is preserved).
    /// </summary>
    public Orientation Orientation
    {
        get => this.orientation;
        set
        {
            if (this.orientation == value)
            {
                return;
            }

            this.orientation = value;
            this.ApplyOrientation();
            foreach (var header in this.headers.Values)
            {
                header.ApplyOrientation();
            }

            this.UpdateStates();
        }
    }

    /// <summary>
    /// Updates every tab text + background tint so the strip blends with
    /// the active terminal color scheme. The supplied colour is treated
    /// as the scheme's foreground (contrast partner of its background)
    /// and reused at varying alphas for the active / inactive / hover
    /// background tints — that way the tabs read correctly on both dark
    /// and light schemes without needing a separate dark-mode branch.
    /// </summary>
    /// <param name="rgb">Foreground colour as a 24-bit RGB integer
    /// (<c>0x00RRGGBB</c>).</param>
    public void ApplyForegroundColor(int rgb)
    {
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);

        this.tabForegroundBrush.Color = Color.FromArgb(TabForegroundAlpha, r, g, b);
        this.mutedForegroundBrush.Color = Color.FromArgb(MutedForegroundAlpha, r, g, b);
        this.inactiveTabBrush.Color = Color.FromArgb(InactiveTintAlpha, r, g, b);
        this.inactiveHoverBrush.Color = Color.FromArgb(InactiveHoverTintAlpha, r, g, b);
        this.activeTabBrush.Color = Color.FromArgb(ActiveTintAlpha, r, g, b);
        this.activeHoverBrush.Color = Color.FromArgb(ActiveHoverTintAlpha, r, g, b);
    }

    private static TabHeader? FindHeaderFromSource(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is TabHeader th)
            {
                return th;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void ApplyOrientation()
    {
        bool vertical = this.orientation == Orientation.Vertical;

        if (vertical)
        {
            this.Width = VerticalRailWidth;
            this.Height = double.NaN;
            this.rootDock.Orientation = Orientation.Vertical;
            this.rootDock.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.tabsPanel.Orientation = Orientation.Vertical;
            this.newTabButton.Width = VerticalRailWidth - 8;
            this.newTabButton.Height = 28;
            this.newTabButton.HorizontalAlignment = HorizontalAlignment.Stretch;

            // The new-tab button sits above the tab list in vertical mode.
            this.EnsureNewTabButtonFirst();

            this.dropIndicator.Width = double.NaN;
            this.dropIndicator.Height = 3;
            this.dropIndicator.Margin = new Thickness(4, 1, 4, 1);
            this.dropIndicator.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.dropIndicator.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            this.Width = double.NaN;
            this.Height = 28;
            this.rootDock.Orientation = Orientation.Horizontal;
            this.rootDock.HorizontalAlignment = HorizontalAlignment.Left;
            this.tabsPanel.Orientation = Orientation.Horizontal;
            this.newTabButton.Width = 48;
            this.newTabButton.Height = 28;
            this.newTabButton.HorizontalAlignment = HorizontalAlignment.Left;

            // Tab list comes before the trailing + button in horizontal mode.
            this.EnsureTabsPanelFirst();

            this.dropIndicator.Width = 3;
            this.dropIndicator.Height = double.NaN;
            this.dropIndicator.Margin = new Thickness(1, 4, 1, 4);
            this.dropIndicator.HorizontalAlignment = HorizontalAlignment.Left;
            this.dropIndicator.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private void EnsureTabsPanelFirst()
    {
        int tabsIdx = this.rootDock.Children.IndexOf(this.tabsPanel);
        int btnIdx = this.rootDock.Children.IndexOf(this.newTabButton);
        if (tabsIdx > btnIdx)
        {
            this.rootDock.Children.Remove(this.tabsPanel);
            this.rootDock.Children.Insert(0, this.tabsPanel);
        }
    }

    private void EnsureNewTabButtonFirst()
    {
        int tabsIdx = this.rootDock.Children.IndexOf(this.tabsPanel);
        int btnIdx = this.rootDock.Children.IndexOf(this.newTabButton);
        if (btnIdx > tabsIdx)
        {
            this.rootDock.Children.Remove(this.newTabButton);
            this.rootDock.Children.Insert(0, this.newTabButton);
        }
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var h in this.headers.Values)
            {
                this.tabsPanel.Children.Remove(h);
            }

            this.headers.Clear();
            if (this.tabView is not null)
            {
                foreach (var t in this.tabView.Tabs)
                {
                    this.AddHeader(t);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Move)
        {
            // ObservableCollection.Move — reorder the existing header without
            // creating / destroying any visuals so transitions stay alive.
            if (e.OldItems is not null && e.OldItems.Count > 0 && e.OldItems[0] is TabSession moved)
            {
                if (this.headers.TryGetValue(moved, out var header))
                {
                    this.tabsPanel.Children.Remove(header);
                    int insertIndex = Math.Clamp(e.NewStartingIndex, 0, this.tabsPanel.Children.Count);
                    this.tabsPanel.Children.Insert(insertIndex, header);
                }
            }
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (TabSession t in e.OldItems)
                {
                    if (this.headers.TryGetValue(t, out var h))
                    {
                        this.tabsPanel.Children.Remove(h);
                        this.headers.Remove(t);
                        h.Detach();
                    }
                }
            }

            if (e.NewItems is not null)
            {
                int insertIndex = e.NewStartingIndex;
                foreach (TabSession t in e.NewItems)
                {
                    this.AddHeader(t, insertIndex++);
                }
            }
        }

        this.UpdateStates();
    }

    private void OnActiveTabChanged(TabSession? newActive)
    {
        this.UpdateStates();
    }

    private void AddHeader(TabSession tab, int index = -1)
    {
        var header = new TabHeader(tab, this);
        header.ActivateRequested += t => this.tabView?.ActivateTab(t);
        header.CloseRequested += t => this.tabView?.CloseTab(t);
        header.DuplicateRequested += t => this.DuplicateTabRequested?.Invoke(t);
        header.GroupAssignmentRequested += (t, g) => this.TabGroupAssignmentRequested?.Invoke(t, g);
        this.headers[tab] = header;
        if (index < 0 || index >= this.tabsPanel.Children.Count)
        {
            this.tabsPanel.Children.Add(header);
        }
        else
        {
            this.tabsPanel.Children.Insert(index, header);
        }
    }

    private void UpdateStates()
    {
        if (this.tabView is null)
        {
            return;
        }

        int count = this.tabView.Tabs.Count;
        var active = this.tabView.ActiveTab;
        foreach (var (tab, header) in this.headers)
        {
            header.SetState(ReferenceEquals(tab, active), count);
        }
    }

    private void OnStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var header = FindHeaderFromSource(e.Source);
        if (header is null || this.tabView is null)
        {
            return;
        }

        int from = this.tabView.Tabs.IndexOf(header.Session);
        if (from < 0)
        {
            return;
        }

        this.drag = new DragState(header.Session, from, e.GetPosition(this), e.Pointer);
    }

    private void OnStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.drag is null)
        {
            return;
        }

        // Defensive: if the left button is no longer down (e.g., a release
        // event was swallowed by a child handler), abandon the drag so we
        // never paint the drop indicator on a plain hover.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.drag = null;
            this.RemoveDropIndicator();
            return;
        }

        var pt = e.GetPosition(this);
        if (!this.drag.Moved)
        {
            var delta = pt - this.drag.Origin;
            if (Math.Abs(delta.X) < DragStartThreshold && Math.Abs(delta.Y) < DragStartThreshold)
            {
                return;
            }

            this.drag.Moved = true;
            this.EnsureDropIndicatorInserted();
            e.Pointer.Capture(this);
        }

        this.UpdateDropIndicator(pt);
    }

    private void OnStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (this.drag is null)
        {
            return;
        }

        var snapshot = this.drag;
        this.drag = null;
        this.RemoveDropIndicator();

        if (!snapshot.Moved)
        {
            return;
        }

        e.Pointer.Capture(null);

        var strippt = e.GetPosition(this);
        if (this.IsOutsideWindow(strippt))
        {
            var screen = this.PointToScreen(strippt);
            this.TabDetachRequested?.Invoke(snapshot.Tab, screen);
            return;
        }

        int to = this.ComputeDropIndex(strippt);

        // StackPanel insert index is "before item N"; when we remove the
        // source first, everything to its right shifts down by one, so we
        // subtract one when dropping further right than the source.
        if (to > snapshot.FromIndex)
        {
            to -= 1;
        }

        if (to != snapshot.FromIndex && this.tabView is not null)
        {
            int max = this.tabView.Tabs.Count - 1;
            this.TabReorderRequested?.Invoke(snapshot.FromIndex, Math.Clamp(to, 0, max));
        }
    }

    private void OnStripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (this.drag is not null)
        {
            this.drag = null;
            this.RemoveDropIndicator();
        }
    }

    private bool IsOutsideWindow(Point stripPoint)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
        {
            return false;
        }

        // Convert the strip-local point to window-client coordinates.
        var winPt = this.TranslatePoint(stripPoint, window) ?? stripPoint;
        var bounds = new Rect(window.ClientSize);
        return !bounds.Contains(winPt);
    }

    private int ComputeDropIndex(Point stripPoint)
    {
        bool vertical = this.orientation == Orientation.Vertical;
        double panelCoord = vertical
            ? stripPoint.Y - this.tabsPanel.Bounds.Y
            : stripPoint.X - this.tabsPanel.Bounds.X;
        int headerCount = 0;
        for (int i = 0; i < this.tabsPanel.Children.Count; i++)
        {
            var child = this.tabsPanel.Children[i];
            if (ReferenceEquals(child, this.dropIndicator))
            {
                continue;
            }

            var b = child.Bounds;
            double mid = vertical ? b.Y + (b.Height / 2.0) : b.X + (b.Width / 2.0);
            if (panelCoord < mid)
            {
                return headerCount;
            }

            headerCount++;
        }

        return headerCount;
    }

    private void UpdateDropIndicator(Point stripPoint)
    {
        if (this.IsOutsideWindow(stripPoint))
        {
            this.dropIndicator.IsVisible = false;
            return;
        }

        int target = this.ComputeDropIndex(stripPoint);
        this.PositionDropIndicator(target);
        this.dropIndicator.IsVisible = true;
    }

    private void EnsureDropIndicatorInserted()
    {
        if (!this.tabsPanel.Children.Contains(this.dropIndicator))
        {
            this.tabsPanel.Children.Add(this.dropIndicator);
        }
    }

    private void PositionDropIndicator(int headerIndex)
    {
        // Translate "insertion before header index N" to a panel child index
        // that skips the indicator itself if it's already inserted.
        this.tabsPanel.Children.Remove(this.dropIndicator);
        int insertAt = 0;
        int headerCount = 0;
        for (int i = 0; i < this.tabsPanel.Children.Count; i++)
        {
            if (headerCount == headerIndex)
            {
                insertAt = i;
                break;
            }

            headerCount++;
            insertAt = i + 1;
        }

        this.tabsPanel.Children.Insert(insertAt, this.dropIndicator);
    }

    private void RemoveDropIndicator()
    {
        this.tabsPanel.Children.Remove(this.dropIndicator);
        this.dropIndicator.IsVisible = false;
    }

    private void RebuildProfileFlyout()
    {
        this.profileFlyout.Items.Clear();
        foreach (var profile in this.profiles)
        {
            var captured = profile;
            var item = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(captured.Name) ? "(unnamed)" : captured.Name,
            };
            item.Click += (_, _) => this.NewTabWithProfileRequested?.Invoke(captured);
            this.profileFlyout.Items.Add(item);
        }

        if (this.profiles.Count > 0)
        {
            this.profileFlyout.Items.Add(new Separator());
        }

        var manage = new MenuItem { Header = "Manage profiles…" };
        manage.Click += (_, _) => this.ManageProfilesRequested?.Invoke();
        this.profileFlyout.Items.Add(manage);
    }

    /// <summary>
    /// Refreshes every header's pill visibility, fill color, and
    /// context-menu "Add to group" submenu entries to reflect the
    /// current <see cref="GroupStore"/> contents.
    /// </summary>
    private void OnGroupsChanged()
    {
        foreach (var header in this.headers.Values)
        {
            header.RefreshGroup();
        }
    }

    /// <summary>
    /// Captures the state of an in-progress tab-strip drag: the dragged
    /// session, its original index, the press origin, and the captured
    /// pointer. Only becomes visually observable once the pointer crosses
    /// <see cref="DragStartThreshold"/>.
    /// </summary>
    private sealed class DragState
    {
        public DragState(TabSession tab, int fromIndex, Point origin, IPointer pointer)
        {
            this.Tab = tab;
            this.FromIndex = fromIndex;
            this.Origin = origin;
            this.Pointer = pointer;
        }

        public TabSession Tab { get; }

        public int FromIndex { get; }

        public Point Origin { get; }

        public IPointer Pointer { get; }

        public bool Moved { get; set; }
    }

    /// <summary>
    /// Individual tab header button with title, close glyph, middle-click
    /// close, active/inactive styling, and an optional colored pill above
    /// the header indicating tab-group membership.
    /// </summary>
    private sealed class TabHeader : Border
    {
        private readonly TabSession tab;
        private readonly TabStrip owner;
        private readonly TextBlock titleBlock;
        private readonly Button closeButton;
        private readonly Rectangle divider;
        private readonly Rectangle groupPill;
        private readonly Rectangle activeIndicator;
        private readonly Grid layoutGrid;
        private bool isActive;
        private bool hasMultipleTabs;

        public TabHeader(TabSession tab, TabStrip owner)
        {
            this.tab = tab;
            this.owner = owner;
            this.Background = owner.inactiveTabBrush;
            this.Transitions = new Transitions
            {
                new BrushTransition
                {
                    Property = Border.BackgroundProperty,
                    Duration = TimeSpan.FromMilliseconds(150),
                },
            };

            this.layoutGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                RowDefinitions = new RowDefinitions("Auto,*"),
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            // Active-tab accent bar. Position and geometry depend on the
            // owning strip's orientation; ApplyOrientation() re-anchors it.
            this.activeIndicator = new Rectangle
            {
                Fill = ActiveAccentBrush,
                IsHitTestVisible = false,
                IsVisible = false,
            };
            Grid.SetRow(this.activeIndicator, 0);
            Grid.SetRowSpan(this.activeIndicator, 2);
            Grid.SetColumn(this.activeIndicator, 0);
            Grid.SetColumnSpan(this.activeIndicator, 4);
            this.layoutGrid.Children.Add(this.activeIndicator);

            this.groupPill = new Rectangle
            {
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 0, 6, 0),
                RadiusX = 1.5,
                RadiusY = 1.5,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            Grid.SetRow(this.groupPill, 0);
            Grid.SetColumn(this.groupPill, 0);
            Grid.SetColumnSpan(this.groupPill, 4);
            this.layoutGrid.Children.Add(this.groupPill);

            this.titleBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = owner.tabForegroundBrush,
                FontSize = 12,
                Text = tab.Title,
            };
            Grid.SetRow(this.titleBlock, 1);
            Grid.SetColumn(this.titleBlock, 1);
            this.layoutGrid.Children.Add(this.titleBlock);

            this.closeButton = new Button
            {
                Content = "\u00D7",
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = owner.mutedForegroundBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(3),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Focusable = false,
                IsVisible = false,
            };
            this.closeButton.PointerEntered += (_, _) => this.closeButton.Background = CloseHoverBrush;
            this.closeButton.PointerExited += (_, _) => this.closeButton.Background = Brushes.Transparent;
            AutomationProperties.SetName(this.closeButton, $"Close tab: {tab.Title}");
            this.closeButton.Click += (_, e) =>
            {
                e.Handled = true;
                this.CloseRequested?.Invoke(this.tab);
            };
            Grid.SetRow(this.closeButton, 1);
            Grid.SetColumn(this.closeButton, 2);
            this.layoutGrid.Children.Add(this.closeButton);

            this.divider = new Rectangle
            {
                Width = 1,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 6),
                Fill = DividerBrush,
                IsVisible = false,
            };
            Grid.SetRow(this.divider, 1);
            Grid.SetColumn(this.divider, 3);
            this.layoutGrid.Children.Add(this.divider);

            this.Child = this.layoutGrid;
            this.ApplyOrientation();

            AutomationProperties.SetName(this, tab.Title);

            this.PointerPressed += this.OnPointerPressed;
            this.PointerEntered += this.OnPointerEntered;
            this.PointerExited += this.OnPointerExited;
            this.AttachContextMenu();
            this.RefreshGroup();
            tab.PropertyChanged += this.OnTabPropertyChanged;
        }

        public event Action<TabSession>? ActivateRequested;

        public event Action<TabSession>? CloseRequested;

        public event Action<TabSession>? DuplicateRequested;

        public event Action<TabSession, string?>? GroupAssignmentRequested;

        /// <summary>Gets the session this header represents.</summary>
        public TabSession Session => this.tab;

        /// <summary>
        /// Gets the colored pill rectangle. Exposed (internal-only via
        /// the enclosing private class contract) so headless UI tests
        /// can assert visibility + fill when a tab joins a group.
        /// </summary>
        public Rectangle GroupPill => this.groupPill;

        public void Detach()
        {
            this.tab.PropertyChanged -= this.OnTabPropertyChanged;
        }

        public void SetState(bool active, int tabCount)
        {
            this.isActive = active;
            this.hasMultipleTabs = tabCount > 1;
            this.Background = this.PickBackgroundBrush();
            this.divider.IsVisible = false;
            this.closeButton.IsVisible = this.hasMultipleTabs && (active || this.IsPointerOver);
            this.activeIndicator.IsVisible = false;
        }

        /// <summary>
        /// Applies the current owner <see cref="TabStrip.Orientation"/>
        /// to this header: swaps size / corner-radius, repositions the
        /// group pill + active accent indicator, and refreshes the
        /// divider visibility rule. Called from the ctor and whenever
        /// the owning strip's orientation changes.
        /// </summary>
        public void ApplyOrientation()
        {
            bool vertical = this.owner.orientation == Orientation.Vertical;
            if (vertical)
            {
                this.Width = double.NaN;
                this.Height = 40;
                this.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.CornerRadius = new CornerRadius(0, 6, 6, 0);
                this.Margin = new Thickness(0, 1, 0, 1);

                // Group pill — keep it as a short coloured strip at the
                // top of the header content so the same visual idea
                // applies, but anchored above the title cell.
                this.groupPill.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.groupPill.VerticalAlignment = VerticalAlignment.Top;
                this.groupPill.Height = 3;
                this.groupPill.Width = double.NaN;
                this.groupPill.Margin = new Thickness(10, 2, 10, 0);

                // Active accent: 3px left-edge bar, full height.
                this.activeIndicator.HorizontalAlignment = HorizontalAlignment.Left;
                this.activeIndicator.VerticalAlignment = VerticalAlignment.Stretch;
                this.activeIndicator.Width = 3;
                this.activeIndicator.Height = double.NaN;
                this.activeIndicator.Margin = new Thickness(0, 4, 0, 4);
            }
            else
            {
                this.Width = 160;
                this.Height = 32;
                this.HorizontalAlignment = HorizontalAlignment.Left;
                this.CornerRadius = new CornerRadius(6);
                this.Margin = new Thickness(2, 3, 2, 3);

                this.groupPill.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.groupPill.VerticalAlignment = VerticalAlignment.Top;
                this.groupPill.Height = 3;
                this.groupPill.Width = double.NaN;
                this.groupPill.Margin = new Thickness(6, 0, 6, 0);

                // Active accent (kept for layout symmetry but hidden by
                // SetState — active state is now signalled by background fill).
                this.activeIndicator.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.activeIndicator.VerticalAlignment = VerticalAlignment.Bottom;
                this.activeIndicator.Width = double.NaN;
                this.activeIndicator.Height = 2;
                this.activeIndicator.Margin = new Thickness(6, 0, 6, 0);
            }
        }

        /// <summary>
        /// Refreshes the group pill's visibility and fill color from
        /// the tab's current <see cref="TabSession.GroupId"/> and the
        /// owning strip's <see cref="TabStrip.GroupStore"/>. Also
        /// rebuilds the context menu so the "Add to group" submenu
        /// reflects the live group list.
        /// </summary>
        public void RefreshGroup()
        {
            TabGroup? group = null;
            var gid = this.tab.GroupId;
            if (!string.IsNullOrEmpty(gid))
            {
                group = this.owner.groupStore?.Find(gid);
            }

            if (group is null)
            {
                this.groupPill.IsVisible = false;
                this.groupPill.Fill = null;
            }
            else
            {
                this.groupPill.Fill = new SolidColorBrush(Color.FromRgb(
                    (byte)((group.Color >> 16) & 0xFF),
                    (byte)((group.Color >> 8) & 0xFF),
                    (byte)(group.Color & 0xFF)));
                this.groupPill.IsVisible = true;
            }

            this.AttachContextMenu();
        }

        private IBrush PickBackgroundBrush()
        {
            bool hover = this.IsPointerOver;
            if (this.isActive)
            {
                return hover ? this.owner.activeHoverBrush : this.owner.activeTabBrush;
            }

            return hover ? this.owner.inactiveHoverBrush : this.owner.inactiveTabBrush;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsMiddleButtonPressed)
            {
                e.Handled = true;
                this.CloseRequested?.Invoke(this.tab);
                return;
            }

            if (props.IsLeftButtonPressed)
            {
                this.ActivateRequested?.Invoke(this.tab);
            }
        }

        private void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            this.Background = this.PickBackgroundBrush();

            if (this.hasMultipleTabs)
            {
                this.closeButton.IsVisible = true;
            }
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            this.Background = this.PickBackgroundBrush();

            if (this.hasMultipleTabs && !this.isActive)
            {
                this.closeButton.IsVisible = false;
            }
        }

        private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabSession.Title))
            {
                this.titleBlock.Text = this.tab.Title;
                AutomationProperties.SetName(this, this.tab.Title);
                AutomationProperties.SetName(this.closeButton, $"Close tab: {this.tab.Title}");
            }
            else if (e.PropertyName == nameof(TabSession.GroupId))
            {
                this.RefreshGroup();
            }
        }

        private void AttachContextMenu()
        {
            var duplicateItem = new MenuItem { Header = "Duplicate tab" };
            duplicateItem.Click += (_, _) => this.DuplicateRequested?.Invoke(this.tab);

            var closeItem = new MenuItem { Header = "Close tab" };
            closeItem.Click += (_, _) => this.CloseRequested?.Invoke(this.tab);

            var addToGroupItem = new MenuItem { Header = "Add to group" };
            var store = this.owner.groupStore;
            if (store is not null)
            {
                foreach (var g in store.Groups)
                {
                    var captured = g;
                    var sub = new MenuItem { Header = captured.Name };
                    sub.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, captured.Id);
                    addToGroupItem.Items.Add(sub);
                }

                if (store.Groups.Count > 0)
                {
                    addToGroupItem.Items.Add(new Separator());
                }
            }

            var newGroupItem = new MenuItem { Header = "New group…" };
            newGroupItem.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, CreateGroupSentinel);
            addToGroupItem.Items.Add(newGroupItem);

            var removeFromGroupItem = new MenuItem
            {
                Header = "Remove from group",
                IsEnabled = !string.IsNullOrEmpty(this.tab.GroupId),
            };
            removeFromGroupItem.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, null);

            var menu = new ContextMenu();
            menu.Items.Add(duplicateItem);
            menu.Items.Add(closeItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(addToGroupItem);
            menu.Items.Add(removeFromGroupItem);
            this.ContextMenu = menu;
        }
    }
}
