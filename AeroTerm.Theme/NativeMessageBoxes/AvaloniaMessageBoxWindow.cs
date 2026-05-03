// <copyright file="AvaloniaMessageBoxWindow.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System.Collections.Generic;
using System.Threading.Tasks;
using AeroTerm.Theme.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// AeroTerm-themed Avalonia fallback window for native message boxes.
/// </summary>
internal sealed class AvaloniaMessageBoxWindow : Window
{
    private readonly List<Button> actionButtons = new();
    private NativeMessageBoxResult result;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaMessageBoxWindow"/> class.
    /// </summary>
    /// <param name="options">The message-box options.</param>
    internal AvaloniaMessageBoxWindow(NativeMessageBoxOptions options)
    {
        this.result = options.CancelResult;
        this.Title = options.Title;
        this.Width = 420;
        this.SizeToContent = SizeToContent.Height;
        this.CanResize = false;
        this.ShowInTaskbar = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Classes.Add("dialog");

        var bodyText = new TextBlock
        {
            Text = options.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        this.AddButtons(options, buttonRow);

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
    /// Gets the current selected result.
    /// </summary>
    internal NativeMessageBoxResult CurrentResult => this.result;

    /// <summary>
    /// Gets the action buttons displayed by the fallback window.
    /// </summary>
    internal IReadOnlyList<Button> ActionButtons => this.actionButtons;

    /// <summary>
    /// Shows the window modally and returns the selected result.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    /// <returns>The selected result.</returns>
    internal async Task<NativeMessageBoxResult> ShowMessageBoxAsync(Window owner)
    {
        var dialogResult = await this.ShowDialog<NativeMessageBoxResult?>(owner);
        return dialogResult ?? this.result;
    }

    private void AddButtons(NativeMessageBoxOptions options, StackPanel buttonRow)
    {
        switch (options.Buttons)
        {
            case NativeMessageBoxButtons.Ok:
                buttonRow.Children.Add(this.CreateButton(
                    options.PrimaryButtonText,
                    NativeMessageBoxResult.Ok,
                    isDefault: true,
                    isCancel: true,
                    isAccent: true));
                break;
            case NativeMessageBoxButtons.YesNo:
                buttonRow.Children.Add(this.CreateButton(
                    options.PrimaryButtonText,
                    NativeMessageBoxResult.Yes,
                    isDefault: false,
                    isCancel: false,
                    isAccent: true));
                buttonRow.Children.Add(this.CreateButton(
                    options.SecondaryButtonText ?? "No",
                    NativeMessageBoxResult.No,
                    isDefault: true,
                    isCancel: true,
                    isAccent: false));
                break;
        }
    }

    private Button CreateButton(
        string text,
        NativeMessageBoxResult buttonResult,
        bool isDefault,
        bool isCancel,
        bool isAccent)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 96,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };

        if (isAccent)
        {
            button.Classes.Add("accent");
        }

        AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            this.Complete(buttonResult);
        };

        this.actionButtons.Add(button);
        return button;
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            this.Complete(this.result);
        }
    }

    private void Complete(NativeMessageBoxResult completedResult)
    {
        this.result = completedResult;
        if (this.IsVisible)
        {
            this.Close(completedResult);
        }
    }
}
