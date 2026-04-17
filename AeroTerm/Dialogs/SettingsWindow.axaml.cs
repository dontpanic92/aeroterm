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

        // Restore persisted size if it is within sensible bounds.
        if (settings.SettingsWindowWidth >= this.MinWidth
            && settings.SettingsWindowHeight >= this.MinHeight)
        {
            this.Width = settings.SettingsWindowWidth;
            this.Height = settings.SettingsWindowHeight;
        }

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

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is SettingsViewModel vm)
        {
            vm.SearchQuery = string.Empty;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Always capture current size so the next open remembers it,
        // regardless of OK/Cancel — window sizing is UI state, not
        // content state.
        this.settings.SettingsWindowWidth = (int)this.Width;
        this.settings.SettingsWindowHeight = (int)this.Height;

        switch (this.CloseReason)
        {
            case Result.Ok:
                // Save update-related settings from the Updates page, if present.
                if (this.DataContext is SettingsViewModel vm)
                {
                    foreach (var page in vm.Pages)
                    {
                        if (page is ViewModels.UpdatesPageViewModel updatesPage)
                        {
                            updatesPage.SaveToSettings();
                        }
                    }
                }

                this.settings.Save();
                break;
            case Result.Cancel:
            case Result.NotClosed:
                this.settings.Reload();

                // Reload would overwrite the captured size; re-apply it so
                // the persisted window geometry is not lost on Cancel.
                this.settings.SettingsWindowWidth = (int)this.Width;
                this.settings.SettingsWindowHeight = (int)this.Height;
                this.settings.Save();
                break;
        }
    }
}
