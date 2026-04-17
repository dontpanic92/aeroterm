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
using Avalonia.Layout;
using Avalonia.Media;

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
    private TabView? tabView;
    private IReadOnlyList<Profile> profiles = new List<Profile>();

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
