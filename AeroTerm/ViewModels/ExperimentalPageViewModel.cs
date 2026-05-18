// <copyright file="ExperimentalPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Services;

/// <summary>
/// View model for experimental opt-in features.
/// </summary>
internal sealed class ExperimentalPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly AppSettings settings;
    private bool enableWorkbench;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExperimentalPageViewModel"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public ExperimentalPageViewModel(AppSettings settings)
    {
        this.settings = settings;
        this.enableWorkbench = settings.EnableWorkbench;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Experimental";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        SettingsSearchLabels.Workbench,
    };

    /// <summary>
    /// Gets or sets a value indicating whether the experimental Workbench is enabled.
    /// </summary>
    public bool EnableWorkbench
    {
        get => this.enableWorkbench;
        set
        {
            if (this.SetField(ref this.enableWorkbench, value))
            {
                this.settings.EnableWorkbench = value;
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}
