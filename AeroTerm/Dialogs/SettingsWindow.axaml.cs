// <copyright file="SettingsWindow.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.Services;
using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

/// <summary>
/// Settings dialog window.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// Used by the XAML designer; runtime code should use
    /// <see cref="SettingsWindow(AppSettings, SettingsViewModel)"/>.
    /// </summary>
    public SettingsWindow()
        : this(AppSettings.Default, new SettingsViewModel(new List<SettingsPageViewModel>().AsReadOnly()))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="viewModel">The settings view model.</param>
    public SettingsWindow(AppSettings settings, SettingsViewModel viewModel)
    {
        this.settings = settings;
        this.DataContext = viewModel;
        this.InitializeComponent();
        this.Closing += this.OnWindowClosing;
    }

    /// <summary>
    /// Reason for closing the window.
    /// </summary>
    public enum Result
    {
        /// <summary>Window is not closed yet.</summary>
        NotClosed,

        /// <summary>Window closed via OK button.</summary>
        Ok,

        /// <summary>Window closed via Cancel button.</summary>
        Cancel,
    }

    /// <summary>
    /// Gets the reason for closing the window.
    /// </summary>
    public Result CloseReason { get; private set; } = Result.NotClosed;

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        this.CloseReason = Result.Ok;
        this.Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        this.CloseReason = Result.Cancel;
        this.Close();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        switch (this.CloseReason)
        {
            case Result.Ok:
                this.settings.Save();
                break;
            case Result.Cancel:
            case Result.NotClosed:
                this.settings.Reload();
                break;
        }
    }
}
