// <copyright file="TabStrip.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThemeNativeContextMenu = AeroTerm.Theme.Controls.NativeContextMenu;
using ThemeNativeMenuFlyout = AeroTerm.Theme.Controls.NativeMenuFlyout;
using ThemeNativeMenuItem = AeroTerm.Theme.Controls.NativeMenuItem;
using ThemeNativeMenuSeparator = AeroTerm.Theme.Controls.NativeMenuSeparator;

/// <summary>
/// Horizontal tab strip for a <see cref="TabView"/>. Renders the active /
/// inactive tab buttons, each with a close (×) affordance, plus a trailing
/// "+" button that raises <see cref="NewTabRequested"/>. Middle-click on a
/// tab closes it. Colour / position transitions animate over 150 ms.
/// <para>
/// The strip is deliberately a separate control from <see cref="TabView"/>
/// so the hosting window can place it inside its title bar row on Windows /
/// Linux, or just past the traffic-light reservation in the macOS titlebar.
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
    /// Perpendicular distance (in pixels) the pointer must travel away
    /// from the tab strip before the drag is treated as a detach / new-window
    /// gesture, even if the cursor is still inside the window.
    /// </summary>
    private const double DetachDistanceThreshold = 80.0;

    /// <summary>
    /// Horizontal rail width in vertical-orientation mode. Narrow enough
    /// to feel like a rail, wide enough to show a sensible title slice.
    /// </summary>
    private const double VerticalRailWidth = 180;

    /// <summary>
    /// Maximum width of a single tab header in horizontal mode, used
    /// when the strip has plenty of room. Matches the historical
    /// fixed-width tab look.
    /// </summary>
    private const double MaxTabWidth = 200;

    /// <summary>
    /// Minimum width a tab header can shrink to before the strip falls
    /// back to horizontal scrolling. Picked so the close glyph and a
    /// few title characters remain readable.
    /// </summary>
    private const double MinTabWidth = 80;

    /// <summary>
    /// Horizontal margin reserved around the trailing "+" / profile
    /// SplitButton when computing the tab-area extent. Mirrors the
    /// button's visual margin so tabs never crowd the SplitButton.
    /// </summary>
    private const double NewTabButtonReservedMargin = 8;

    /// <summary>
    /// Width of each scroll-indicator button (◀ / ▶) docked at the
    /// leading and trailing edges of the tab row when the tab list
    /// overflows the available width.
    /// </summary>
    private const double ScrollButtonWidth = 20;

    /// <summary>
    /// Distance in pixels the tab list scrolls per click on one of the
    /// scroll-indicator buttons. Matches <see cref="MinTabWidth"/> so
    /// each click reveals roughly one additional tab.
    /// </summary>
    private const double ScrollButtonStep = MinTabWidth;

    private const byte TabForegroundAlpha = 0xF0;
    private const byte MutedForegroundAlpha = 0x90;
    private const byte InactiveTintAlpha = 0x10;
    private const byte InactiveHoverTintAlpha = 0x22;
    private const byte ActiveTintAlpha = 0x30;
    private const byte ActiveHoverTintAlpha = 0x45;

    /// <summary>
    /// Duration of the smooth-scroll animation triggered by the
    /// scroll-indicator buttons. Matches the 150 ms used for tab header
    /// background transitions.
    /// </summary>
    private static readonly TimeSpan ScrollAnimationDuration = TimeSpan.FromMilliseconds(150);

    private readonly IBrush dividerBrush;
    private readonly IBrush closeHoverBrush;
    private readonly IBrush activeAccentBrush;

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

    private readonly TabHeaderPanel tabsPanel;
    private readonly SplitButton newTabButton;
    private readonly RepeatButton scrollLeftButton;
    private readonly RepeatButton scrollRightButton;
    private readonly ThemeNativeMenuFlyout profileFlyout;
    private readonly Dictionary<TabSession, TabHeader> headers = new();
    private readonly DockPanel rootDock;
    private readonly ScrollViewer tabsScroller;
    private DispatcherTimer? scrollAnimTimer;
    private double scrollAnimStartX;
    private double scrollAnimTargetX;
    private long scrollAnimStartTicks;
    private TabView? tabView;
    private IReadOnlyList<Profile> profiles = new List<Profile>();
    private string? defaultProfileId;
    private TabGroupStore? groupStore;
    private DragState? drag;
    private DragPreviewWindow? dragPreview;
    private TabStrip? dragTargetStrip;
    private Border? externalDropIndicator;
    private int externalDropLastIndex = -1;
    private DispatcherTimer? dragSettleTimer;
    private Orientation orientation = Orientation.Horizontal;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabStrip"/> class.
    /// </summary>
    public TabStrip()
    {
        this.Focusable = false;
        this.dividerBrush = this.ResolveThemeBrush("TabStripDividerBrush", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        this.closeHoverBrush = this.ResolveThemeBrush("TabStripCloseHoverBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        this.activeAccentBrush = this.ResolveThemeBrush("TabStripActiveAccentBrush", Color.FromArgb(0xFF, 0x4F, 0xA3, 0xFF));
        this.InstallTabCloseButtonStyles();
        this.tabsPanel = new TabHeaderPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        this.newTabButton = new SplitButton
        {
            Content = BuildPlusIcon(this.tabForegroundBrush),
            Width = 48,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brushes.Transparent,
            Foreground = this.tabForegroundBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false,
        };

        // Re-skin the SplitButton's per-state brushes so the "+" primary
        // button and the chevron secondary button track the tab strip's
        // own foreground / hover / pressed brushes.
        this.RefreshNewTabButtonStateBrushes();

        this.profileFlyout = new ThemeNativeMenuFlyout();
        this.newTabButton.Flyout = this.profileFlyout;
        AutomationProperties.SetName(this.newTabButton, "New tab");
        this.newTabButton.Click += (_, _) => this.NewTabRequested?.Invoke();
        this.RebuildProfileFlyout();

        // ScrollViewer wraps the tab list so it can shrink-then-scroll
        // once tabs hit MinTabWidth. Vertical wheel events are translated
        // to horizontal scroll in horizontal mode (see OnTabsScrollerWheel).
        this.tabsScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Content = this.tabsPanel,
        };
        this.tabsScroller.AddHandler(PointerWheelChangedEvent, this.OnTabsScrollerWheel, RoutingStrategies.Tunnel);
        this.tabsScroller.ScrollChanged += (_, _) => this.UpdateScrollButtonVisibility();

        // Scroll-indicator buttons flanking the tab scroller. They use
        // RepeatButton so holding down keeps scrolling, and are only
        // visible when the tab list overflows (horizontal mode).
        this.scrollLeftButton = this.BuildScrollButton(isLeft: true);
        this.scrollLeftButton.Click += (_, _) => this.ScrollTabList(-ScrollButtonStep);

        this.scrollRightButton = this.BuildScrollButton(isLeft: false);
        this.scrollRightButton.Click += (_, _) => this.ScrollTabList(ScrollButtonStep);

        // DockPanel keeps the SplitButton pinned to the trailing edge
        // (right in horizontal, top in vertical) so the "+" / profile
        // menu can never overflow outside the strip's allocated column.
        this.rootDock = new DockPanel
        {
            LastChildFill = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        DockPanel.SetDock(this.newTabButton, Dock.Right);
        DockPanel.SetDock(this.scrollRightButton, Dock.Right);
        DockPanel.SetDock(this.scrollLeftButton, Dock.Left);
        this.rootDock.Children.Add(this.newTabButton);
        this.rootDock.Children.Add(this.scrollRightButton);
        this.rootDock.Children.Add(this.scrollLeftButton);
        this.rootDock.Children.Add(this.tabsScroller);

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
    /// Raised when the user drops a dragged tab onto another window's
    /// <see cref="TabStrip"/>. Subscribers typically call
    /// <see cref="TabView.DetachTab"/> and transfer the session to the
    /// target <see cref="MainWindow"/> at the specified insertion index.
    /// </summary>
    public event Action<TabSession, MainWindow, int>? TabTransferRequested;

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
    /// Gets or sets the id of the profile that should be highlighted as
    /// the default in the "+" button's dropdown. The default entry is
    /// promoted to the top of the menu and rendered with a
    /// "<c> (default)</c>" suffix. When <c>null</c> or unknown the menu
    /// renders in source order without a marker.
    /// </summary>
    public string? DefaultProfileId
    {
        get => this.defaultProfileId;
        set
        {
            if (this.defaultProfileId == value)
            {
                return;
            }

            this.defaultProfileId = value;
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

        // The SplitButton template resolves its per-state background and
        // foreground via local resources. Refresh the locally-scoped
        // overrides whenever the palette changes so the "+" / menu button
        // keeps tracking the tabs.
        this.RefreshNewTabButtonStateBrushes();
        this.RefreshScrollButtonStateBrushes(this.scrollLeftButton);
        this.RefreshScrollButtonStateBrushes(this.scrollRightButton);
    }

    /// <summary>
    /// Tests whether <paramref name="screenPos"/> falls within this
    /// strip's tab panel and, if so, returns the insertion index where a
    /// dropped tab would land. Returns <c>-1</c> if the point is outside
    /// the panel.
    /// </summary>
    /// <param name="screenPos">Pointer position in screen pixels.</param>
    /// <returns>Zero-based insertion index, or <c>-1</c>.</returns>
    internal int GetDropIndexAtScreenPoint(PixelPoint screenPos)
    {
        var stripPoint = this.PointToClient(screenPos);
        if (!this.Bounds.Contains(new Point(stripPoint.X, stripPoint.Y)))
        {
            return -1;
        }

        return this.ComputeDropIndex(new Point(stripPoint.X, stripPoint.Y));
    }

    /// <summary>
    /// Shows a thin accent-coloured insertion indicator at the specified
    /// position in the tab panel, signalling to the user where a
    /// cross-window tab drop would land.
    /// </summary>
    /// <param name="index">Zero-based insertion index.</param>
    internal void ShowExternalDropIndicator(int index)
    {
        if (index == this.externalDropLastIndex && this.externalDropIndicator is not null)
        {
            return;
        }

        if (this.externalDropIndicator is null)
        {
            this.externalDropIndicator = new Border
            {
                Background = this.activeAccentBrush,
                IsHitTestVisible = false,
                ZIndex = 2,
            };
        }

        bool vertical = this.orientation == Orientation.Vertical;
        if (vertical)
        {
            this.externalDropIndicator.Width = double.NaN;
            this.externalDropIndicator.Height = 3;
        }
        else
        {
            this.externalDropIndicator.Width = 3;
            this.externalDropIndicator.Height = double.NaN;
        }

        // Compute the pixel position for the indicator by examining
        // the bounds of the panel's header children.
        double pos = 0;
        int headerCount = 0;
        for (int i = 0; i < this.tabsPanel.Children.Count; i++)
        {
            if (this.tabsPanel.Children[i] is not TabHeader th)
            {
                continue;
            }

            if (headerCount == index)
            {
                pos = vertical ? th.Bounds.Y : th.Bounds.X;
                break;
            }

            headerCount++;
            pos = vertical
                ? th.Bounds.Y + th.Bounds.Height + th.Margin.Top + th.Margin.Bottom
                : th.Bounds.X + th.Bounds.Width + th.Margin.Left + th.Margin.Right;
        }

        if (vertical)
        {
            this.externalDropIndicator.Margin = new Thickness(4, pos - 1.5, 4, 0);
            this.externalDropIndicator.VerticalAlignment = VerticalAlignment.Top;
            this.externalDropIndicator.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            this.externalDropIndicator.Margin = new Thickness(pos - 1.5, 4, 0, 4);
            this.externalDropIndicator.HorizontalAlignment = HorizontalAlignment.Left;
            this.externalDropIndicator.VerticalAlignment = VerticalAlignment.Stretch;
        }

        if (!this.tabsPanel.Children.Contains(this.externalDropIndicator))
        {
            this.tabsPanel.Children.Add(this.externalDropIndicator);
        }

        this.externalDropLastIndex = index;
    }

    /// <summary>
    /// Removes the external drop indicator if currently shown.
    /// </summary>
    internal void ClearExternalDropIndicator()
    {
        if (this.externalDropIndicator is not null)
        {
            this.tabsPanel.Children.Remove(this.externalDropIndicator);
            this.externalDropIndicator = null;
            this.externalDropLastIndex = -1;
        }
    }

    /// <summary>
    /// Propagates the strip's actual available extent (minus the
    /// trailing "+" SplitButton) down to the inner
    /// <see cref="TabHeaderPanel"/> so it can shrink-to-fit before the
    /// wrapping <see cref="ScrollViewer"/> hands it infinite space and
    /// every tab snaps back to <see cref="MaxTabWidth"/>.
    /// </summary>
    /// <param name="availableSize">Size offered by the parent layout.
    /// </param>
    /// <returns>Desired size determined by the base UserControl
    /// implementation.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        bool horizontal = this.orientation == Orientation.Horizontal;
        if (horizontal)
        {
            double avail = availableSize.Width;
            if (double.IsInfinity(avail) || double.IsNaN(avail))
            {
                this.tabsPanel.AvailableTabExtent = double.PositiveInfinity;
            }
            else
            {
                double reserved = this.newTabButton.Width
                    + this.newTabButton.Margin.Left + this.newTabButton.Margin.Right
                    + NewTabButtonReservedMargin;

                // Account for scroll-indicator buttons when they are visible
                // so the tab-area shrinks to avoid overlap.
                if (this.scrollLeftButton.IsVisible)
                {
                    reserved += ScrollButtonWidth;
                }

                if (this.scrollRightButton.IsVisible)
                {
                    reserved += ScrollButtonWidth;
                }

                this.tabsPanel.AvailableTabExtent = Math.Max(0, avail - reserved);
            }
        }
        else
        {
            // Vertical mode: tabs keep their natural height; the
            // wrapping ScrollViewer scrolls the rail when overflowed.
            this.tabsPanel.AvailableTabExtent = double.PositiveInfinity;
        }

        var result = base.MeasureOverride(availableSize);
        this.UpdateScrollButtonVisibility();
        return result;
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

    /// <summary>
    /// Builds the vector "+" glyph used as the <see cref="SplitButton"/>'s
    /// primary content. Sized to match the chevron on the secondary side
    /// (12 × 12) so the two glyphs read as a balanced pair.
    /// </summary>
    /// <returns>A new <see cref="PathIcon"/> instance.</returns>
    /// <param name="foreground">Brush used to paint the icon.</param>
    private static PathIcon BuildPlusIcon(IBrush foreground)
    {
        return new PathIcon
        {
            Width = 12,
            Height = 12,
            Data = Geometry.Parse(
                "M484,128 H540 V484 H896 V540 H540 V896 H484 V540 H128 V484 H484 Z"),
            Foreground = foreground,
        };
    }

    private IBrush ResolveThemeBrush(string key, Color fallback)
    {
        if (this.TryGetResource(key, this.ActualThemeVariant, out var value))
        {
            if (value is IBrush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(fallback);
    }

    /// <summary>
    /// Builds a scroll-indicator <see cref="RepeatButton"/> with a
    /// left-pointing or right-pointing chevron glyph. The button is
    /// initially hidden and only becomes visible via
    /// <see cref="UpdateScrollButtonVisibility"/> when the tab list
    /// overflows.
    /// </summary>
    /// <param name="isLeft"><see langword="true"/> for the left (◀)
    /// button; <see langword="false"/> for right (▶).</param>
    /// <returns>A new <see cref="RepeatButton"/> instance.</returns>
    private RepeatButton BuildScrollButton(bool isLeft)
    {
        // Left chevron: ‹   Right chevron: ›
        // Drawn in a 1024×1024 coordinate space like BuildPlusIcon.
        string chevronData = isLeft
            ? "M640,128 L256,512 640,896"
            : "M384,128 L768,512 384,896";

        var icon = new PathIcon
        {
            Width = 10,
            Height = 10,
            Data = Geometry.Parse(chevronData),
        };

        var btn = new RepeatButton
        {
            Content = icon,
            Width = ScrollButtonWidth,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = this.tabForegroundBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsVisible = false,
            Interval = 120,
            Delay = 300,
        };

        AutomationProperties.SetName(btn, isLeft ? "Scroll tabs left" : "Scroll tabs right");
        this.RefreshScrollButtonStateBrushes(btn);
        return btn;
    }

    /// <summary>
    /// Applies per-state resource overrides to a scroll-indicator button
    /// so its hover / pressed states track the tab strip palette.
    /// </summary>
    /// <param name="btn">The scroll button to theme.</param>
    private void RefreshScrollButtonStateBrushes(RepeatButton btn)
    {
        var resources = btn.Resources;
        resources["ControlFillHoverBrush"] = this.inactiveHoverBrush;
        resources["ControlFillPressedBrush"] = this.inactiveTabBrush;
        resources["ControlBorderHoverBrush"] = Brushes.Transparent;
        resources["TextPrimaryBrush"] = this.tabForegroundBrush;
    }

    /// <summary>
    /// Overrides the brush tokens consumed by the <see cref="SplitButton"/>
    /// template at the button's local resource scope so the trailing "+" /
    /// menu button paints with the tab strip's own foreground / hover /
    /// pressed brushes instead of the global theme palette.
    /// </summary>
    private void RefreshNewTabButtonStateBrushes()
    {
        var resources = this.newTabButton.Resources;

        // Hover background for the inner primary and secondary buttons.
        resources["AeroTermSplitButtonPartHoverBrush"] = this.inactiveHoverBrush;

        // Pressed / flyout-open / checked background for the inner buttons.
        resources["AeroTermSplitButtonPartPressedBrush"] = this.inactiveTabBrush;

        // Foreground in every interactive state.
        resources["AeroTermSplitButtonPartForegroundBrush"] = this.tabForegroundBrush;
        resources["AeroTermSplitButtonSeparatorBrush"] = Brushes.Transparent;
    }

    /// <summary>
    /// Installs hover/pressed styles for per-tab close buttons (class
    /// "tab-close"). The default <see cref="Button"/> ControlTheme sets
    /// the templated ContentPresenter's Background to
    /// <c>ControlFillHoverBrush</c> on hover, which would otherwise win
    /// over a plain <c>Button.Background</c> assignment and hide the
    /// scheme-derived close-hover tint. Targeting the templated
    /// ContentPresenter at matching specificity ensures the close button
    /// uses <see cref="closeHoverBrush"/> on every theme.
    /// </summary>
    private void InstallTabCloseButtonStyles()
    {
        Style restStyle = new Style(s => s
            .OfType<Button>().Class("tab-close")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        restStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent));
        restStyle.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));

        Style hoverStyle = new Style(s => s
            .OfType<Button>().Class("tab-close").Class(":pointerover")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        hoverStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, this.closeHoverBrush));
        hoverStyle.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));

        Style pressedStyle = new Style(s => s
            .OfType<Button>().Class("tab-close").Class(":pressed")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        pressedStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, this.closeHoverBrush));
        pressedStyle.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));

        this.Styles.Add(restStyle);
        this.Styles.Add(hoverStyle);
        this.Styles.Add(pressedStyle);
    }

    /// <summary>
    /// Initiates a smooth animated scroll of the tab list by
    /// <paramref name="delta"/> pixels. Positive values scroll right
    /// (revealing trailing tabs); negative scroll left. If an animation
    /// is already running, the target is updated additively so repeated
    /// clicks (or RepeatButton holds) accumulate smoothly.
    /// </summary>
    /// <param name="delta">Pixel distance to scroll.</param>
    private void ScrollTabList(double delta)
    {
        double maxX = Math.Max(0, this.tabsScroller.Extent.Width - this.tabsScroller.Viewport.Width);
        double currentX = this.tabsScroller.Offset.X;

        // When an animation is already in flight, extend the target from
        // where it was headed rather than from the current (mid-ease)
        // position. This keeps rapid RepeatButton clicks additive.
        double baseTarget = this.scrollAnimTimer is not null
            ? this.scrollAnimTargetX
            : currentX;

        double newTarget = Math.Clamp(baseTarget + delta, 0, maxX);

        this.scrollAnimStartX = currentX;
        this.scrollAnimTargetX = newTarget;
        this.scrollAnimStartTicks = Environment.TickCount64;

        if (this.scrollAnimTimer is null)
        {
            this.scrollAnimTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            this.scrollAnimTimer.Tick += this.OnScrollAnimTick;
            this.scrollAnimTimer.Start();
        }
    }

    /// <summary>
    /// Per-frame tick handler for the smooth-scroll animation. Applies
    /// an ease-out curve to interpolate the scroller offset from
    /// <see cref="scrollAnimStartX"/> to <see cref="scrollAnimTargetX"/>
    /// over <see cref="ScrollAnimationDuration"/>.
    /// </summary>
    private void OnScrollAnimTick(object? sender, EventArgs e)
    {
        long elapsed = Environment.TickCount64 - this.scrollAnimStartTicks;
        double totalMs = ScrollAnimationDuration.TotalMilliseconds;
        double t = Math.Clamp(elapsed / totalMs, 0, 1);

        // Ease-out quad: decelerates as it approaches the target.
        double eased = 1 - ((1 - t) * (1 - t));

        double x = this.scrollAnimStartX + ((this.scrollAnimTargetX - this.scrollAnimStartX) * eased);
        this.tabsScroller.Offset = new Vector(x, this.tabsScroller.Offset.Y);

        if (t >= 1)
        {
            this.scrollAnimTimer?.Stop();
            this.scrollAnimTimer = null;
        }
    }

    /// <summary>
    /// Shows or hides the scroll-indicator buttons based on the current
    /// scroll state. Called after tab-list changes, scroll events, and
    /// layout passes.
    /// </summary>
    private void UpdateScrollButtonVisibility()
    {
        if (this.orientation != Orientation.Horizontal)
        {
            this.scrollLeftButton.IsVisible = false;
            this.scrollRightButton.IsVisible = false;
            return;
        }

        double extent = this.tabsScroller.Extent.Width;
        double viewport = this.tabsScroller.Viewport.Width;
        bool overflows = extent > viewport + 0.5;

        if (!overflows)
        {
            this.scrollLeftButton.IsVisible = false;
            this.scrollRightButton.IsVisible = false;
            return;
        }

        double offset = this.tabsScroller.Offset.X;
        double maxX = Math.Max(0, extent - viewport);
        this.scrollLeftButton.IsVisible = offset > 0.5;
        this.scrollRightButton.IsVisible = offset < maxX - 0.5;
    }

    private void ApplyOrientation()
    {
        bool vertical = this.orientation == Orientation.Vertical;

        if (vertical)
        {
            this.Width = VerticalRailWidth;
            this.Height = double.NaN;
            this.HorizontalAlignment = HorizontalAlignment.Left;
            this.tabsPanel.Orientation = Orientation.Vertical;
            this.newTabButton.Width = VerticalRailWidth - 8;
            this.newTabButton.Height = 28;
            this.newTabButton.HorizontalAlignment = HorizontalAlignment.Stretch;

            // The new-tab button sits above the tab list in vertical mode;
            // the scroller fills the remainder so a tall list scrolls.
            DockPanel.SetDock(this.newTabButton, Dock.Top);
            this.tabsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            this.tabsScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

            // Scroll buttons are not used in vertical rail mode.
            this.scrollLeftButton.IsVisible = false;
            this.scrollRightButton.IsVisible = false;
        }
        else
        {
            this.Width = double.NaN;
            this.Height = 28;
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.tabsPanel.Orientation = Orientation.Horizontal;
            this.newTabButton.Width = 48;
            this.newTabButton.Height = 28;
            this.newTabButton.HorizontalAlignment = HorizontalAlignment.Center;

            // Trailing "+" stays pinned at the right edge so it can never
            // overflow past the tab strip's allocated column.
            DockPanel.SetDock(this.newTabButton, Dock.Right);
            this.tabsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            this.tabsScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            this.UpdateScrollButtonVisibility();
        }
    }

    private void OnTabsScrollerWheel(object? sender, PointerWheelEventArgs e)
    {
        // In horizontal mode, funnel both vertical (Y) and horizontal
        // (X) wheel deltas into horizontal scrolling of the tab list —
        // a trackpad swipe sideways or a mouse-wheel scroll should both
        // pan the row. Skip when the content fits entirely so wheel
        // events bubble to whatever else cares about them.
        if (this.orientation != Orientation.Horizontal)
        {
            return;
        }

        if (this.tabsScroller.Extent.Width <= this.tabsScroller.Viewport.Width)
        {
            return;
        }

        // Sum both axes so a two-finger diagonal trackpad gesture still
        // scrolls. Sign convention matches ScrollViewer's own handler:
        // positive delta -> offset decreases (content moves right).
        double delta = e.Delta.Y + e.Delta.X;
        if (delta == 0)
        {
            return;
        }

        const double Step = 48;
        var offset = this.tabsScroller.Offset;
        double maxX = Math.Max(0, this.tabsScroller.Extent.Width - this.tabsScroller.Viewport.Width);
        double newX = Math.Clamp(offset.X - (delta * Step), 0, maxX);
        this.tabsScroller.Offset = new Vector(newX, offset.Y);
        e.Handled = true;
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Cancel any in-progress drag when the tab collection changes
        // (except for Move, which is fired by our own reorder).
        if (this.drag is not null && e.Action != NotifyCollectionChangedAction.Move)
        {
            this.drag = null;
            this.ClearAllDragTransforms();
        }

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
        this.ScrollActiveIntoView();
    }

    /// <summary>
    /// Ensures the currently active tab header is visible in the
    /// horizontal tab scroller. Deferred to the next dispatcher pass
    /// so the freshly-added header has been measured and arranged
    /// before its bounds are queried.
    /// </summary>
    private void ScrollActiveIntoView()
    {
        if (this.orientation != Orientation.Horizontal || this.tabView is null)
        {
            return;
        }

        var active = this.tabView.ActiveTab;
        if (active is null || !this.headers.TryGetValue(active, out var header))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!header.IsAttachedToVisualTree())
                {
                    return;
                }

                double extent = this.tabsScroller.Extent.Width;
                double viewport = this.tabsScroller.Viewport.Width;
                if (extent <= viewport)
                {
                    return;
                }

                var origin = header.TranslatePoint(default, this.tabsPanel);
                if (origin is null)
                {
                    return;
                }

                double headerLeft = origin.Value.X;
                double headerRight = headerLeft + header.Bounds.Width;
                double offsetX = this.tabsScroller.Offset.X;
                double maxX = Math.Max(0, extent - viewport);

                double newOffset = offsetX;
                if (headerLeft < offsetX)
                {
                    newOffset = headerLeft;
                }
                else if (headerRight > offsetX + viewport)
                {
                    newOffset = headerRight - viewport;
                }

                newOffset = Math.Clamp(newOffset, 0, maxX);
                if (!newOffset.Equals(offsetX))
                {
                    this.tabsScroller.Offset = new Vector(newOffset, this.tabsScroller.Offset.Y);
                }
            },
            DispatcherPriority.Loaded);
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

        var active = this.tabView.ActiveTab;
        foreach (var (tab, header) in this.headers)
        {
            header.SetState(ReferenceEquals(tab, active));
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

        this.drag = new DragState(header.Session, from, e.GetPosition(this), e.Pointer, header);
    }

    private void OnStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.drag is null)
        {
            return;
        }

        // Defensive: if the left button is no longer down (e.g., a release
        // event was swallowed by a child handler), abandon the drag so we
        // never leave transforms on a plain hover.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.drag = null;
            this.ClearAllDragTransforms();
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
            this.BeginDragVisuals();
            e.Pointer.Capture(this);
        }

        this.UpdateDragPosition(pt);
    }

    private void OnStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (this.drag is null)
        {
            return;
        }

        var snapshot = this.drag;
        this.drag = null;

        if (!snapshot.Moved)
        {
            return;
        }

        e.Pointer.Capture(null);

        var strippt = e.GetPosition(this);

        // Clear all drag visuals before committing the reorder so the
        // headers land at their new layout positions immediately.
        this.ClearAllDragTransforms();

        if (this.ShouldDetach(strippt))
        {
            var screen = this.PointToScreen(strippt);
            var sourceWindow = TopLevel.GetTopLevel(this) as Window;

            // Check if the pointer is over another window's tab strip.
            if (sourceWindow is not null)
            {
                var target = DragDropCoordinator.FindDropTarget(screen, sourceWindow);
                if (target.HasValue && TopLevel.GetTopLevel(target.Value.TargetStrip) is MainWindow targetWindow)
                {
                    target.Value.TargetStrip.ClearExternalDropIndicator();
                    this.TabTransferRequested?.Invoke(snapshot.Tab, targetWindow, target.Value.InsertionIndex);
                    return;
                }
            }

            this.TabDetachRequested?.Invoke(snapshot.Tab, screen);
            return;
        }

        int to = this.ComputeDropIndex(strippt, snapshot.FromIndex);

        // ComputeDropIndex returns a full-list insertion index. When
        // dropping further right than the source, subtract one because
        // ObservableCollection.Move removes the source first, shifting
        // everything to its right down by one.
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
            this.ClearAllDragTransforms();
        }
    }

    /// <summary>
    /// Sets up the initial drag visuals: lifts the dragged tab and adds
    /// displacement transitions to all neighbour headers so they can
    /// slide out of the way smoothly.
    /// </summary>
    private void BeginDragVisuals()
    {
        var d = this.drag!;
        bool vertical = this.orientation == Orientation.Vertical;
        double slotSize = vertical
            ? d.Header.Bounds.Height + d.Header.Margin.Top + d.Header.Margin.Bottom
            : d.Header.Bounds.Width + d.Header.Margin.Left + d.Header.Margin.Right;
        d.SlotSize = slotSize;

        // Lift the dragged tab above its siblings.
        d.Header.Opacity = 0.85;
        d.Header.ZIndex = 1;

        // Pre-cache the displacement transforms used by neighbours.
        if (vertical)
        {
            d.ShiftPositive = TransformOperations.Parse(
                FormattableString.Invariant($"translate(0px,{(int)Math.Round(slotSize)}px)"));
            d.ShiftNegative = TransformOperations.Parse(
                FormattableString.Invariant($"translate(0px,{-(int)Math.Round(slotSize)}px)"));
        }
        else
        {
            d.ShiftPositive = TransformOperations.Parse(
                FormattableString.Invariant($"translate({(int)Math.Round(slotSize)}px,0px)"));
            d.ShiftNegative = TransformOperations.Parse(
                FormattableString.Invariant($"translate({-(int)Math.Round(slotSize)}px,0px)"));
        }

        // Add the displacement transition to every non-dragged header and
        // set a baseline identity transform so the first shift animates
        // from (0,0) instead of from null.
        foreach (var (tab, header) in this.headers)
        {
            if (ReferenceEquals(tab, d.Tab))
            {
                continue;
            }

            header.Transitions ??= new Transitions();
            if (!header.Transitions.Contains(DragState.DisplacementTransition))
            {
                header.Transitions.Add(DragState.DisplacementTransition);
            }

            header.RenderTransform = DragState.IdentityTranslate;
        }
    }

    /// <summary>
    /// Updates the dragged tab's position under the cursor and computes
    /// displacement offsets for neighbouring headers.
    /// </summary>
    /// <param name="stripPoint">Current pointer position in strip
    /// coordinates.</param>
    private void UpdateDragPosition(Point stripPoint)
    {
        var d = this.drag!;
        bool vertical = this.orientation == Orientation.Vertical;

        // Move the dragged header to follow the cursor.
        double pointerDelta = vertical
            ? stripPoint.Y - d.Origin.Y
            : stripPoint.X - d.Origin.X;
        if (vertical)
        {
            d.Header.RenderTransform = TransformOperations.Parse(
                FormattableString.Invariant($"translate(0px,{(int)Math.Round(pointerDelta)}px)"));
        }
        else
        {
            d.Header.RenderTransform = TransformOperations.Parse(
                FormattableString.Invariant($"translate({(int)Math.Round(pointerDelta)}px,0px)"));
        }

        // When outside the window bounds, show the drag preview and check
        // for cross-window drop targets. Reset neighbour displacements so
        // the source strip looks clean.
        if (this.ShouldDetach(stripPoint))
        {
            if (d.LastFullDropIndex != -1)
            {
                d.LastFullDropIndex = -1;
                this.ResetNeighbourTransforms(d);
            }

            var screenPos = this.PointToScreen(stripPoint);
            this.UpdateCrossWindowDragState(screenPos, d);
            return;
        }

        // Pointer returned inside the source window — tear down any
        // cross-window preview / indicator that was active.
        this.ClearCrossWindowDragState();

        // Determine where the tab would land and displace neighbours.
        int fullDropIndex = this.ComputeDropIndex(stripPoint, d.FromIndex);
        if (fullDropIndex == d.LastFullDropIndex)
        {
            return;
        }

        d.LastFullDropIndex = fullDropIndex;

        int source = d.FromIndex;
        int vdi = fullDropIndex > source ? fullDropIndex - 1 : fullDropIndex;

        foreach (var (tab, header) in this.headers)
        {
            if (ReferenceEquals(tab, d.Tab))
            {
                continue;
            }

            int oi = this.tabView!.Tabs.IndexOf(tab);
            if (oi < 0)
            {
                continue;
            }

            int vi = oi < source ? oi : oi - 1;
            int targetSlot = vi < vdi ? vi : vi + 1;
            int shift = targetSlot - oi;

            if (shift > 0)
            {
                header.RenderTransform = d.ShiftPositive;
            }
            else if (shift < 0)
            {
                header.RenderTransform = d.ShiftNegative;
            }
            else
            {
                header.RenderTransform = DragState.IdentityTranslate;
            }
        }
    }

    /// <summary>
    /// Resets all neighbour headers back to identity transform during
    /// drag (e.g. when the pointer leaves the window).
    /// </summary>
    /// <param name="d">Active drag state.</param>
    private void ResetNeighbourTransforms(DragState d)
    {
        foreach (var (tab, header) in this.headers)
        {
            if (ReferenceEquals(tab, d.Tab))
            {
                continue;
            }

            header.RenderTransform = DragState.IdentityTranslate;
        }
    }

    /// <summary>
    /// Clears all drag-related visual state: transforms, opacity, z-index,
    /// and the displacement transition from every header.
    /// </summary>
    private void ClearAllDragTransforms()
    {
        this.dragSettleTimer?.Stop();
        this.dragSettleTimer = null;

        foreach (var (_, header) in this.headers)
        {
            header.Transitions?.Remove(DragState.DisplacementTransition);
            header.RenderTransform = null;
            header.Opacity = 1.0;
            header.ZIndex = 0;
        }

        this.ClearCrossWindowDragState();
    }

    /// <summary>
    /// Updates the drag preview and cross-window drop indicator while the
    /// pointer is outside the source window.
    /// </summary>
    /// <param name="screenPos">Current pointer position in screen pixels.</param>
    /// <param name="d">Active drag state.</param>
    private void UpdateCrossWindowDragState(PixelPoint screenPos, DragState d)
    {
        // Lazily create and show the floating preview window.
        if (this.dragPreview is null)
        {
            this.dragPreview = new DragPreviewWindow(d.Tab.Title ?? string.Empty);
            this.dragPreview.Show();
        }

        this.dragPreview.MoveToScreenPosition(screenPos);

        // Check whether the pointer is over another window's tab strip.
        var sourceWindow = TopLevel.GetTopLevel(this) as Window;
        DragDropCoordinator.DropTarget? target = sourceWindow is not null
            ? DragDropCoordinator.FindDropTarget(screenPos, sourceWindow)
            : null;

        if (target.HasValue)
        {
            var ts = target.Value.TargetStrip;
            int idx = target.Value.InsertionIndex;

            // Clear indicator on previously-targeted strip if it changed.
            if (this.dragTargetStrip is not null && !ReferenceEquals(this.dragTargetStrip, ts))
            {
                this.dragTargetStrip.ClearExternalDropIndicator();
            }

            ts.ShowExternalDropIndicator(idx);
            this.dragTargetStrip = ts;
            this.dragPreview.IsMergeMode = true;
        }
        else
        {
            if (this.dragTargetStrip is not null)
            {
                this.dragTargetStrip.ClearExternalDropIndicator();
                this.dragTargetStrip = null;
            }

            this.dragPreview.IsMergeMode = false;
        }
    }

    /// <summary>
    /// Tears down the floating preview window and any cross-window drop
    /// indicators.
    /// </summary>
    private void ClearCrossWindowDragState()
    {
        if (this.dragPreview is not null)
        {
            this.dragPreview.Close();
            this.dragPreview = null;
        }

        if (this.dragTargetStrip is not null)
        {
            this.dragTargetStrip.ClearExternalDropIndicator();
            this.dragTargetStrip = null;
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

    /// <summary>
    /// Returns <c>true</c> when the pointer is far enough from the tab
    /// strip (perpendicular to its orientation axis) to be treated as a
    /// detach gesture, even if the cursor has not left the window.
    /// </summary>
    /// <param name="stripPoint">Pointer position in strip-local coords.</param>
    /// <returns><c>true</c> when the perpendicular distance exceeds
    /// <see cref="DetachDistanceThreshold"/>.</returns>
    private bool IsFarFromTabRow(Point stripPoint)
    {
        var stripBounds = new Rect(this.Bounds.Size);
        bool vertical = this.orientation == Orientation.Vertical;

        // Measure perpendicular distance from the strip's own bounds.
        double distance;
        if (vertical)
        {
            // Vertical strip: measure horizontal distance.
            if (stripPoint.X < stripBounds.X)
            {
                distance = stripBounds.X - stripPoint.X;
            }
            else if (stripPoint.X > stripBounds.Right)
            {
                distance = stripPoint.X - stripBounds.Right;
            }
            else
            {
                distance = 0;
            }
        }
        else
        {
            // Horizontal strip: measure vertical distance.
            if (stripPoint.Y < stripBounds.Y)
            {
                distance = stripBounds.Y - stripPoint.Y;
            }
            else if (stripPoint.Y > stripBounds.Bottom)
            {
                distance = stripPoint.Y - stripBounds.Bottom;
            }
            else
            {
                distance = 0;
            }
        }

        return distance >= DetachDistanceThreshold;
    }

    /// <summary>
    /// Returns <c>true</c> when the pointer has left the window entirely
    /// or has moved far enough from the tab row to signal a detach.
    /// </summary>
    /// <param name="stripPoint">Pointer position in strip-local coords.</param>
    /// <returns><c>true</c> when a detach gesture is detected.</returns>
    private bool ShouldDetach(Point stripPoint) =>
        this.IsOutsideWindow(stripPoint) || this.IsFarFromTabRow(stripPoint);

    private int ComputeDropIndex(Point stripPoint, int sourceIndex = -1)
    {
        bool vertical = this.orientation == Orientation.Vertical;

        // tabsPanel sits inside a ScrollViewer, so its Bounds are
        // expressed relative to the scroller's content presenter, not
        // the strip itself. TranslatePoint gives us the panel origin in
        // strip coordinates while also accounting for any scroll offset.
        var origin = this.tabsPanel.TranslatePoint(default, this) ?? default;
        double panelCoord = vertical
            ? stripPoint.Y - origin.Y
            : stripPoint.X - origin.X;
        int headerCount = 0;
        for (int i = 0; i < this.tabsPanel.Children.Count; i++)
        {
            var child = this.tabsPanel.Children[i];
            if (child is not TabHeader)
            {
                continue;
            }

            var b = child.Bounds;

            // When a source index is known, use the near edge of each
            // neighbour instead of its midpoint so that the reorder
            // triggers as soon as the pointer reaches the neighbour's
            // boundary rather than after crossing halfway through it.
            double threshold;
            if (sourceIndex >= 0 && headerCount < sourceIndex)
            {
                // Neighbour is before the source — use its trailing edge.
                threshold = vertical ? b.Y + b.Height : b.X + b.Width;
            }
            else if (sourceIndex >= 0 && headerCount > sourceIndex)
            {
                // Neighbour is after the source — use its leading edge.
                threshold = vertical ? b.Y : b.X;
            }
            else
            {
                // The dragged tab itself, or no source info — midpoint.
                threshold = vertical ? b.Y + (b.Height / 2.0) : b.X + (b.Width / 2.0);
            }

            if (panelCoord < threshold)
            {
                return headerCount;
            }

            headerCount++;
        }

        return headerCount;
    }

    private void RebuildProfileFlyout()
    {
        this.profileFlyout.Items.Clear();

        // Promote the default profile to the top of the menu so the
        // primary one-click new-tab action is always immediately
        // visible. Order is otherwise preserved.
        IEnumerable<Profile> ordered = this.profiles;
        if (!string.IsNullOrEmpty(this.defaultProfileId))
        {
            ordered = this.profiles
                .OrderBy(p => p.Id == this.defaultProfileId ? 0 : 1);
        }

        foreach (var profile in ordered)
        {
            var captured = profile;
            string name = string.IsNullOrWhiteSpace(captured.Name) ? "(unnamed)" : captured.Name;
            bool isDefault = !string.IsNullOrEmpty(this.defaultProfileId)
                && captured.Id == this.defaultProfileId;
            var item = new ThemeNativeMenuItem
            {
                Header = isDefault ? name + " (default)" : name,
            };
            item.Click += (_, _) => this.NewTabWithProfileRequested?.Invoke(captured);
            this.profileFlyout.Items.Add(item);
        }

        if (this.profiles.Count > 0)
        {
            this.profileFlyout.Items.Add(new ThemeNativeMenuSeparator());
        }

        var manage = new ThemeNativeMenuItem { Header = "Manage profiles…" };
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
    /// Custom <see cref="StackPanel"/> for tab headers that shrinks
    /// every <see cref="TabHeader"/> uniformly between
    /// <see cref="TabStrip.MinTabWidth"/> and
    /// <see cref="TabStrip.MaxTabWidth"/> in horizontal mode based on
    /// the strip's actual available extent (set via
    /// <see cref="AvailableTabExtent"/>). Once headers reach the
    /// minimum, the panel reports its full natural width so the
    /// wrapping <see cref="ScrollViewer"/> activates and lets the user
    /// scroll the overflow.
    /// </summary>
    private sealed class TabHeaderPanel : StackPanel
    {
        private double availableTabExtent = double.PositiveInfinity;

        /// <summary>
        /// Gets or sets the soft maximum tab-area extent published by
        /// the owning <see cref="TabStrip"/>. Used in place of
        /// <see cref="MeasureOverride"/>'s incoming width when a
        /// <see cref="ScrollViewer"/> ancestor passes infinity in the
        /// scrolled axis.
        /// </summary>
        /// <remarks>
        /// Setting this property invalidates measure when the value
        /// changes. Without this, resizing the window (e.g., maximize)
        /// would not re-flow tab widths because the inner ScrollViewer
        /// keeps passing infinity in the scrolled axis — Avalonia would
        /// see no measure-input change on this panel and skip layout,
        /// leaving tabs stuck at their previous (narrow) per-header width.
        /// </remarks>
        public double AvailableTabExtent
        {
            get => this.availableTabExtent;
            set
            {
                if (this.availableTabExtent.Equals(value))
                {
                    return;
                }

                this.availableTabExtent = value;
                this.InvalidateMeasure();
                this.InvalidateArrange();
            }
        }

        /// <inheritdoc />
        protected override Size MeasureOverride(Size availableSize)
        {
            bool horizontal = this.Orientation == Orientation.Horizontal;
            int headerCount = 0;
            foreach (var child in this.Children)
            {
                if (child is TabHeader)
                {
                    headerCount++;
                }
            }

            double perHeader = MaxTabWidth;
            if (horizontal && headerCount > 0)
            {
                double soft = availableSize.Width;
                if (double.IsInfinity(soft) || double.IsNaN(soft))
                {
                    soft = this.AvailableTabExtent;
                }

                if (!double.IsInfinity(soft))
                {
                    perHeader = Math.Clamp(soft / headerCount, MinTabWidth, MaxTabWidth);
                }
            }

            double totalMain = 0;
            double maxCross = 0;
            foreach (var child in this.Children)
            {
                Size childAvail;
                if (child is TabHeader)
                {
                    if (horizontal)
                    {
                        childAvail = new Size(perHeader, availableSize.Height);
                    }
                    else
                    {
                        childAvail = new Size(availableSize.Width, double.PositiveInfinity);
                    }
                }
                else
                {
                    childAvail = availableSize;
                }

                child.Measure(childAvail);
                var ds = child.DesiredSize;

                if (horizontal)
                {
                    double w = child is TabHeader ? perHeader : ds.Width;
                    totalMain += w;
                    maxCross = Math.Max(maxCross, ds.Height);
                }
                else
                {
                    totalMain += ds.Height;
                    maxCross = Math.Max(maxCross, ds.Width);
                }
            }

            return horizontal
                ? new Size(totalMain, maxCross)
                : new Size(maxCross, totalMain);
        }

        /// <inheritdoc />
        protected override Size ArrangeOverride(Size finalSize)
        {
            bool horizontal = this.Orientation == Orientation.Horizontal;
            int headerCount = 0;
            foreach (var child in this.Children)
            {
                if (child is TabHeader)
                {
                    headerCount++;
                }
            }

            double perHeader = MaxTabWidth;
            if (horizontal && headerCount > 0)
            {
                // finalSize.Width is whatever the ScrollViewer decided to
                // give us — either the viewport (no scroll) or our full
                // desired width (scroll active). Either way, share-of-
                // viewport is the right way to compute per-tab width.
                double soft = this.AvailableTabExtent;
                if (double.IsInfinity(soft))
                {
                    soft = finalSize.Width;
                }

                perHeader = Math.Clamp(soft / headerCount, MinTabWidth, MaxTabWidth);
            }

            double pos = 0;
            foreach (var child in this.Children)
            {
                if (child is TabHeader)
                {
                    if (horizontal)
                    {
                        child.Arrange(new Rect(pos, 0, perHeader, finalSize.Height));
                        pos += perHeader;
                    }
                    else
                    {
                        double h = child.DesiredSize.Height;
                        child.Arrange(new Rect(0, pos, finalSize.Width, h));
                        pos += h;
                    }
                }
                else
                {
                    var ds = child.DesiredSize;
                    if (horizontal)
                    {
                        child.Arrange(new Rect(pos, 0, ds.Width, finalSize.Height));
                        pos += ds.Width;
                    }
                    else
                    {
                        child.Arrange(new Rect(0, pos, finalSize.Width, ds.Height));
                        pos += ds.Height;
                    }
                }
            }

            return finalSize;
        }
    }

    /// <summary>
    /// Captures the state of an in-progress tab-strip drag: the dragged
    /// session, its original index, the press origin, the captured
    /// pointer, and pre-computed displacement transforms for neighbour
    /// animation. Only becomes visually observable once the pointer
    /// crosses <see cref="DragStartThreshold"/>.
    /// </summary>
    private sealed class DragState
    {
        /// <summary>
        /// Shared transition applied to non-dragged headers so their
        /// displacement animates smoothly. Defined once and reused
        /// across all drag operations.
        /// </summary>
        public static readonly TransformOperationsTransition DisplacementTransition = new()
        {
            Property = Visual.RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new QuadraticEaseOut(),
        };

        /// <summary>
        /// Identity translate used as the baseline / reset value for
        /// headers. Must be a <see cref="TransformOperations"/> instance
        /// (not <c>null</c>) so the <see cref="DisplacementTransition"/>
        /// can interpolate from it.
        /// </summary>
        public static readonly ITransform IdentityTranslate =
            TransformOperations.Parse("translate(0px,0px)");

        /// <summary>
        /// Initializes a new instance of the <see cref="DragState"/> class.
        /// </summary>
        /// <param name="tab">The tab session being dragged.</param>
        /// <param name="fromIndex">Original zero-based index.</param>
        /// <param name="origin">Pointer press position in strip coords.</param>
        /// <param name="pointer">Captured pointer.</param>
        /// <param name="header">The visual header being dragged.</param>
        public DragState(TabSession tab, int fromIndex, Point origin, IPointer pointer, TabHeader header)
        {
            this.Tab = tab;
            this.FromIndex = fromIndex;
            this.Origin = origin;
            this.Pointer = pointer;
            this.Header = header;
        }

        /// <summary>Gets the tab being dragged.</summary>
        public TabSession Tab { get; }

        /// <summary>Gets the original tab index at drag start.</summary>
        public int FromIndex { get; }

        /// <summary>Gets the pointer-press origin in strip coords.</summary>
        public Point Origin { get; }

        /// <summary>Gets the captured pointer.</summary>
        public IPointer Pointer { get; }

        /// <summary>Gets the dragged tab's header control.</summary>
        public TabHeader Header { get; }

        /// <summary>Gets or sets a value indicating whether the drag threshold was crossed.</summary>
        public bool Moved { get; set; }

        /// <summary>Gets or sets the slot size (width in horizontal, height in vertical).</summary>
        public double SlotSize { get; set; }

        /// <summary>Gets or sets the last computed full-list drop index, or -1 if none.</summary>
        public int LastFullDropIndex { get; set; } = -1;

        /// <summary>Gets or sets the pre-parsed positive shift transform (one slot forward).</summary>
        public ITransform? ShiftPositive { get; set; }

        /// <summary>Gets or sets the pre-parsed negative shift transform (one slot backward).</summary>
        public ITransform? ShiftNegative { get; set; }
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
        private readonly TabTitlePresenter titleBlock;
        private readonly Button closeButton;
        private readonly Rectangle divider;
        private readonly Rectangle groupPill;
        private readonly Rectangle activeIndicator;
        private readonly Grid layoutGrid;
        private bool isActive;

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
                Fill = owner.activeAccentBrush,
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

            this.titleBlock = new TabTitlePresenter
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ForegroundBrush = owner.tabForegroundBrush,
                TitleFontSize = 12,
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

            // Hover/pressed background is supplied by TabStrip.InstallTabCloseButtonStyles
            // via the "tab-close" class, which targets the templated ContentPresenter at
            // the same selector specificity as the default Button ControlTheme. Setting
            // Background on the Button itself would lose to the theme's per-state
            // ContentPresenter setter and the scheme-derived hover tint would never show.
            this.closeButton.Classes.Add("tab-close");
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
                Fill = owner.dividerBrush,
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

        public void SetState(bool active)
        {
            this.isActive = active;
            this.Background = this.PickBackgroundBrush();
            this.divider.IsVisible = false;
            this.closeButton.IsVisible = active || this.IsPointerOver;
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
                this.MinWidth = 0;
                this.MaxWidth = double.PositiveInfinity;
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
                // Width is driven by the owning TabHeaderPanel which
                // shrinks all tabs uniformly between MinTabWidth and
                // MaxTabWidth based on available room. The header
                // intentionally has no MinWidth/MaxWidth of its own so
                // the panel's slot size is the sole source of truth and
                // adjacent tabs never visually overlap.
                this.Width = double.NaN;
                this.MinWidth = 0;
                this.MaxWidth = double.PositiveInfinity;
                this.Height = 32;
                this.HorizontalAlignment = HorizontalAlignment.Stretch;
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

            this.closeButton.IsVisible = true;
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            this.Background = this.PickBackgroundBrush();

            if (!this.isActive)
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
            var duplicateItem = new ThemeNativeMenuItem { Header = "Duplicate tab" };
            duplicateItem.Click += (_, _) => this.DuplicateRequested?.Invoke(this.tab);

            var closeItem = new ThemeNativeMenuItem { Header = "Close tab" };
            closeItem.Click += (_, _) => this.CloseRequested?.Invoke(this.tab);

            var addToGroupItem = new ThemeNativeMenuItem { Header = "Add to group" };
            var store = this.owner.groupStore;
            if (store is not null)
            {
                foreach (var g in store.Groups)
                {
                    var captured = g;
                    var sub = new ThemeNativeMenuItem { Header = captured.Name };
                    sub.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, captured.Id);
                    addToGroupItem.Items.Add(sub);
                }

                if (store.Groups.Count > 0)
                {
                    addToGroupItem.Items.Add(new ThemeNativeMenuSeparator());
                }
            }

            var newGroupItem = new ThemeNativeMenuItem { Header = "New group…" };
            newGroupItem.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, CreateGroupSentinel);
            addToGroupItem.Items.Add(newGroupItem);

            var removeFromGroupItem = new ThemeNativeMenuItem
            {
                Header = "Remove from group",
                IsEnabled = !string.IsNullOrEmpty(this.tab.GroupId),
            };
            removeFromGroupItem.Click += (_, _) => this.GroupAssignmentRequested?.Invoke(this.tab, null);

            var menu = new ThemeNativeContextMenu();
            menu.Items.Add(duplicateItem);
            menu.Items.Add(closeItem);
            menu.Items.Add(new ThemeNativeMenuSeparator());
            menu.Items.Add(addToGroupItem);
            menu.Items.Add(removeFromGroupItem);
            ThemeNativeContextMenu.SetMenu(this, menu);
        }
    }
}
