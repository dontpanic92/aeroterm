// <copyright file="ConfirmCloseDialog.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.Resources;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Minimal modal "Are you sure you want to close {N} tabs?" dialog used by
/// <see cref="MainWindow"/> when closing a window with multiple open tabs.
/// Esc / window close button / the "Cancel" button all cancel; only the
/// "Close" button confirms. Enter activates Cancel (the safer default).
/// </summary>
internal sealed class ConfirmCloseDialog : Window
{
    private bool confirmed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmCloseDialog"/> class.
    /// </summary>
    /// <param name="tabCount">Number of open tabs to include in the message.</param>
    public ConfirmCloseDialog(int tabCount)
    {
        this.Title = Strings.ConfirmCloseTitle;
        this.Width = 380;
        this.SizeToContent = SizeToContent.Height;
        this.CanResize = false;
        this.ShowInTaskbar = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var bodyText = new TextBlock
        {
            Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.ConfirmCloseMessageFormat, tabCount),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var closeButton = new Button
        {
            Content = Strings.ButtonClose,
            Width = 96,
            IsDefault = false,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x65, 0x5A)),
        };
        AutomationProperties.SetName(closeButton, Strings.ConfirmCloseAllTabs);
        closeButton.Click += (_, _) =>
        {
            this.confirmed = true;
            this.Close(true);
        };

        var cancelButton = new Button
        {
            Content = Strings.ButtonCancel,
            Width = 96,
            IsDefault = true,
            IsCancel = true,
        };
        AutomationProperties.SetName(cancelButton, Strings.ButtonCancel);
        cancelButton.Click += (_, _) => this.Close(false);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttonRow.Children.Add(closeButton);
        buttonRow.Children.Add(cancelButton);

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
        };
        layout.Children.Add(bodyText);
        layout.Children.Add(buttonRow);

        this.Content = layout;

        this.KeyDown += this.OnKeyDownHandler;
    }

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/> and returns
    /// <c>true</c> when the user explicitly confirmed the close.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    /// <returns><c>true</c> if the user clicked "Close"; otherwise <c>false</c>.</returns>
    public async Task<bool> ShowConfirmAsync(Window owner)
    {
        var result = await this.ShowDialog<bool?>(owner);
        return this.confirmed || result == true;
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            this.Close(false);
        }
    }
}
