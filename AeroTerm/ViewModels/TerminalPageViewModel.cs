// <copyright file="TerminalPageViewModel.cs">
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
/// View model for terminal behavior settings.
/// </summary>
internal sealed class TerminalPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly AppSettings settings;
    private BellAction bellAction;
    private int scrollbackLines;
    private bool middleClickPastes;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalPageViewModel"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public TerminalPageViewModel(AppSettings settings)
    {
        this.settings = settings;
        this.bellAction = settings.BellAction;
        this.scrollbackLines = settings.ScrollbackLines;
        this.middleClickPastes = settings.MiddleClickPastes;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Terminal";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        SettingsSearchLabels.Bell,
        SettingsSearchLabels.ScrollbackLines,
        SettingsSearchLabels.MiddleClickPaste,
    };

    /// <summary>
    /// Gets the available bell-action choices for data binding.
    /// </summary>
    public IReadOnlyList<BellAction> BellActions { get; } = new[]
    {
        BellAction.None,
        BellAction.Visual,
        BellAction.Audio,
        BellAction.Notification,
        BellAction.VisualAndAudio,
        BellAction.All,
    };

    /// <summary>
    /// Gets or sets how the app reacts to the terminal BEL character.
    /// </summary>
    public BellAction BellAction
    {
        get => this.bellAction;
        set
        {
            if (this.SetField(ref this.bellAction, value))
            {
                this.settings.BellAction = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of lines retained in the terminal's scrollback
    /// ring. Range is <c>0..1_000_000</c>; <c>0</c> disables scrollback.
    /// </summary>
    public int ScrollbackLines
    {
        get => this.scrollbackLines;
        set
        {
            int clamped = Math.Clamp(value, 0, 1_000_000);
            if (this.SetField(ref this.scrollbackLines, clamped))
            {
                this.settings.ScrollbackLines = clamped;
                this.OnPropertyChanged(nameof(this.ScrollbackDisabledWarningVisible));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the "scrollback disabled" warning
    /// should be shown next to the scrollback input.
    /// </summary>
    public bool ScrollbackDisabledWarningVisible => this.scrollbackLines == 0;

    /// <summary>
    /// Gets or sets a value indicating whether middle-click inside the
    /// terminal pastes text.
    /// </summary>
    public bool MiddleClickPastes
    {
        get => this.middleClickPastes;
        set
        {
            if (this.SetField(ref this.middleClickPastes, value))
            {
                this.settings.MiddleClickPastes = value;
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
