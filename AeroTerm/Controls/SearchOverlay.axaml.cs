// <copyright file="SearchOverlay.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// The translucent search bar anchored top-right of <see cref="TerminalControl"/>.
/// Owns the query text, the three option toggles, the match counter, and
/// the prev/next/close buttons. It raises strongly-typed events the
/// terminal control subscribes to — the overlay itself holds no terminal
/// state.
/// </summary>
internal partial class SearchOverlay : UserControl
{
    private TextBox? queryBox;
    private TextBlock? statusText;
    private ToggleButton? caseToggle;
    private ToggleButton? regexToggle;
    private ToggleButton? wordToggle;
    private Button? prevButton;
    private Button? nextButton;
    private Button? closeButton;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOverlay"/> class.
    /// </summary>
    public SearchOverlay()
    {
        this.InitializeComponent();

        this.queryBox = this.FindControl<TextBox>("QueryTextBox");
        this.statusText = this.FindControl<TextBlock>("StatusText");
        this.caseToggle = this.FindControl<ToggleButton>("CaseToggle");
        this.regexToggle = this.FindControl<ToggleButton>("RegexToggle");
        this.wordToggle = this.FindControl<ToggleButton>("WordToggle");
        this.prevButton = this.FindControl<Button>("PrevButton");
        this.nextButton = this.FindControl<Button>("NextButton");
        this.closeButton = this.FindControl<Button>("CloseButton");

        if (this.queryBox is not null)
        {
            this.queryBox.TextChanged += this.OnQueryTextChanged;
            this.queryBox.KeyDown += this.OnQueryKeyDown;
        }

        if (this.caseToggle is not null)
        {
            this.caseToggle.IsCheckedChanged += this.OnOptionChanged;
        }

        if (this.regexToggle is not null)
        {
            this.regexToggle.IsCheckedChanged += this.OnOptionChanged;
        }

        if (this.wordToggle is not null)
        {
            this.wordToggle.IsCheckedChanged += this.OnOptionChanged;
        }

        if (this.prevButton is not null)
        {
            this.prevButton.Click += (_, _) => this.NavigateRequested?.Invoke(this, forward: false);
        }

        if (this.nextButton is not null)
        {
            this.nextButton.Click += (_, _) => this.NavigateRequested?.Invoke(this, forward: true);
        }

        if (this.closeButton is not null)
        {
            this.closeButton.Click += (_, _) => this.CloseRequested?.Invoke(this, System.EventArgs.Empty);
        }
    }

    /// <summary>
    /// A prev/next navigation callback. <c>forward = true</c> advances to
    /// the next match (wraps past the end); <c>false</c> retreats.
    /// </summary>
    /// <param name="sender">The overlay.</param>
    /// <param name="forward">Whether to advance forward.</param>
    public delegate void NavigateHandler(object? sender, bool forward);

    /// <summary>
    /// Raised whenever the query text or any of the three option toggles
    /// changes. Handlers should recompute matches.
    /// </summary>
    public event System.EventHandler? QueryChanged;

    /// <summary>
    /// Raised when the user presses prev/next or invokes
    /// Enter / Shift+Enter inside the query box.
    /// </summary>
    public event NavigateHandler? NavigateRequested;

    /// <summary>
    /// Raised when the user dismisses the overlay (Esc or close button).
    /// </summary>
    public event System.EventHandler? CloseRequested;

    /// <summary>
    /// Gets the current query text (never <see langword="null"/>).
    /// </summary>
    public string Query => this.queryBox?.Text ?? string.Empty;

    /// <summary>
    /// Gets the current options composed from the three toggles.
    /// </summary>
    public SearchOptions CurrentOptions => new(
        Regex: this.regexToggle?.IsChecked ?? false,
        CaseSensitive: this.caseToggle?.IsChecked ?? false,
        WholeWord: this.wordToggle?.IsChecked ?? false);

    /// <summary>
    /// Shows the overlay and focuses the query box. Existing text is
    /// selected so typing replaces it.
    /// </summary>
    public void Open()
    {
        this.IsVisible = true;
        if (this.queryBox is not null)
        {
            this.queryBox.Focus();
            this.queryBox.SelectAll();
        }
    }

    /// <summary>
    /// Hides the overlay. Query text and toggle state are preserved for
    /// the next open.
    /// </summary>
    public void Close()
    {
        this.IsVisible = false;
    }

    /// <summary>
    /// Updates the counter text (e.g. "3 / 27"). A total of zero is
    /// rendered as "0 / 0" regardless of the active index value.
    /// </summary>
    /// <param name="activeIndex">1-based index of the active match, or 0
    /// when none is active.</param>
    /// <param name="totalMatches">Total number of matches.</param>
    public void SetStatus(int activeIndex, int totalMatches)
    {
        if (this.statusText is not null)
        {
            int shown = totalMatches == 0 ? 0 : System.Math.Max(1, activeIndex);
            this.statusText.Text = $"{shown} / {totalMatches}";
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnQueryTextChanged(object? sender, TextChangedEventArgs e)
    {
        this.QueryChanged?.Invoke(this, System.EventArgs.Empty);
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e)
    {
        this.QueryChanged?.Invoke(this, System.EventArgs.Empty);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.CloseRequested?.Invoke(this, System.EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            bool forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            this.NavigateRequested?.Invoke(this, forward);
            e.Handled = true;
        }
    }
}
