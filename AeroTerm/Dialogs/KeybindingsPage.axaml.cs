// <copyright file="KeybindingsPage.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.Services;
using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

/// <summary>
/// Keybindings settings page. Each row captures a chord via a
/// press-to-record <see cref="Button"/> that keeps keyboard focus.
/// </summary>
public partial class KeybindingsPage : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingsPage"/> class.
    /// </summary>
    public KeybindingsPage()
    {
        this.InitializeComponent();
    }

    private static bool IsPureModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    private void ChordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeybindingRow row })
        {
            row.IsRecording = true;
        }
    }

    private void ChordButton_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Button { DataContext: KeybindingRow row })
        {
            return;
        }

        if (!row.IsRecording)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            row.IsRecording = false;
            e.Handled = true;
            return;
        }

        // Ignore pure modifier presses — wait for a real key.
        if (IsPureModifier(e.Key))
        {
            return;
        }

        if (this.DataContext is KeybindingsPageViewModel vm)
        {
            vm.CaptureChord(row, new KeyChord(e.KeyModifiers, e.Key));
            row.IsRecording = false;
            e.Handled = true;
        }
    }

    private void ResetRowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeybindingRow row }
            && this.DataContext is KeybindingsPageViewModel vm)
        {
            vm.ResetRow(row);
        }
    }

    private void ResetAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is KeybindingsPageViewModel vm)
        {
            vm.ResetAll();
        }
    }
}
