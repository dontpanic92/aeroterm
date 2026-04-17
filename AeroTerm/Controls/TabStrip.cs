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
    private const double DragStartThreshold = 5.0;

    private static readonly IBrush ActiveTabBrush = Brushes.Transparent;
    private static readonly IBrush InactiveTabBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x2A, 0x2A, 0x2E));
    private static readonly IBrush InactiveHoverBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x40, 0x40, 0x48));
    private static readonly IBrush TabForeground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush MutedForeground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush CloseHoverBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel tabsPanel;
    private readonly SplitButton newTabButton;
    private readonly MenuFlyout profileFlyout;
    private readonly Dictionary<TabSession, TabHeader> headers = new();
    private readonly Rectangle dropIndicator;
    private TabView? tabView;
    private IReadOnlyList<Profile> profiles = new List<Profile>();
    private DragState? drag;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabStrip"/> class.
    /// </summary>
    public TabStrip()
    {
        this.Height = 28;
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
            Foreground = TabForeground,
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
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xA3, 0xFF)),
            IsVisible = false,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var rootDock = new DockPanel
        {
            LastChildFill = false,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        DockPanel.SetDock(this.tabsPanel, Dock.Left);
        DockPanel.SetDock(this.newTabButton, Dock.Left);
        rootDock.Children.Add(this.tabsPanel);
        rootDock.Children.Add(this.newTabButton);

        this.Content = rootDock;

        // Drag handling — bubble so TabHeader still activates on press,
        // then we see the event and track movement for potential drag.
        this.AddHandler(PointerPressedEvent, this.OnStripPointerPressed, RoutingStrategies.Bubble);
        this.AddHandler(PointerMovedEvent, this.OnStripPointerMoved, RoutingStrategies.Bubble);
        this.AddHandler(PointerReleasedEvent, this.OnStripPointerReleased, RoutingStrategies.Bubble);
        this.AddHandler(PointerCaptureLostEvent, this.OnStripPointerCaptureLost, RoutingStrategies.Bubble);
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
        var header = new TabHeader(tab);
        header.ActivateRequested += t => this.tabView?.ActivateTab(t);
        header.CloseRequested += t => this.tabView?.CloseTab(t);
        header.DuplicateRequested += t => this.DuplicateTabRequested?.Invoke(t);
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

        int to = this.ComputeDropIndex(strippt.X);

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

    private int ComputeDropIndex(double stripX)
    {
        double panelX = stripX - this.tabsPanel.Bounds.X;
        int headerCount = 0;
        for (int i = 0; i < this.tabsPanel.Children.Count; i++)
        {
            var child = this.tabsPanel.Children[i];
            if (ReferenceEquals(child, this.dropIndicator))
            {
                continue;
            }

            var b = child.Bounds;
            if (panelX < b.X + (b.Width / 2.0))
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

        int target = this.ComputeDropIndex(stripPoint.X);
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
    /// close, and active/inactive styling.
    /// </summary>
    private sealed class TabHeader : Border
    {
        private readonly TabSession tab;
        private readonly TextBlock titleBlock;
        private readonly Button closeButton;
        private readonly Rectangle divider;
        private bool isActive;
        private bool hasMultipleTabs;

        public TabHeader(TabSession tab)
        {
            this.tab = tab;
            this.Width = 160;
            this.Height = 28;
            this.CornerRadius = new CornerRadius(6, 6, 0, 0);
            this.Background = InactiveTabBrush;
            this.Transitions = new Transitions
            {
                new BrushTransition
                {
                    Property = Border.BackgroundProperty,
                    Duration = TimeSpan.FromMilliseconds(150),
                },
            };
            this.Cursor = new Cursor(StandardCursorType.Hand);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            this.titleBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = TabForeground,
                FontSize = 12,
                Text = tab.Title,
            };
            Grid.SetColumn(this.titleBlock, 0);
            grid.Children.Add(this.titleBlock);

            this.closeButton = new Button
            {
                Content = "\u00D7",
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = MutedForeground,
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
            Grid.SetColumn(this.closeButton, 1);
            grid.Children.Add(this.closeButton);

            this.divider = new Rectangle
            {
                Width = 1,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 6),
                Fill = DividerBrush,
                IsVisible = false,
            };
            Grid.SetColumn(this.divider, 2);
            grid.Children.Add(this.divider);

            this.Child = grid;

            AutomationProperties.SetName(this, tab.Title);

            this.PointerPressed += this.OnPointerPressed;
            this.PointerEntered += this.OnPointerEntered;
            this.PointerExited += this.OnPointerExited;
            this.AttachContextMenu();
            tab.PropertyChanged += this.OnTabPropertyChanged;
        }

        public event Action<TabSession>? ActivateRequested;

        public event Action<TabSession>? CloseRequested;

        public event Action<TabSession>? DuplicateRequested;

        /// <summary>Gets the session this header represents.</summary>
        public TabSession Session => this.tab;

        public void Detach()
        {
            this.tab.PropertyChanged -= this.OnTabPropertyChanged;
        }

        public void SetState(bool active, int tabCount)
        {
            this.isActive = active;
            this.hasMultipleTabs = tabCount > 1;
            this.Background = active ? ActiveTabBrush : InactiveTabBrush;
            this.divider.IsVisible = active && this.hasMultipleTabs;
            this.closeButton.IsVisible = this.hasMultipleTabs && (active || this.IsPointerOver);
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
            if (!this.isActive)
            {
                this.Background = InactiveHoverBrush;
            }

            if (this.hasMultipleTabs)
            {
                this.closeButton.IsVisible = true;
            }
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (!this.isActive)
            {
                this.Background = InactiveTabBrush;
            }

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
        }

        private void AttachContextMenu()
        {
            var duplicateItem = new MenuItem { Header = "Duplicate tab" };
            duplicateItem.Click += (_, _) => this.DuplicateRequested?.Invoke(this.tab);

            var closeItem = new MenuItem { Header = "Close tab" };
            closeItem.Click += (_, _) => this.CloseRequested?.Invoke(this.tab);

            var menu = new ContextMenu();
            menu.Items.Add(duplicateItem);
            menu.Items.Add(closeItem);
            this.ContextMenu = menu;
        }
    }
}
