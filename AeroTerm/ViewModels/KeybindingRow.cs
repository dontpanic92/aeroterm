// <copyright file="KeybindingRow.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Services;

/// <summary>
/// A single row in the Keybindings settings page: one action plus its
/// currently-bound chord (as DSL text).
/// </summary>
public sealed class KeybindingRow : INotifyPropertyChanged
{
    private string chordText = string.Empty;
    private bool isCustomized;
    private bool isRecording;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingRow"/> class.
    /// </summary>
    /// <param name="action">The bound action.</param>
    /// <param name="displayName">The human-readable action name.</param>
    public KeybindingRow(KeybindingAction action, string displayName)
    {
        this.Action = action;
        this.DisplayName = displayName;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the bound action.</summary>
    public KeybindingAction Action { get; }

    /// <summary>Gets the display name shown in the UI.</summary>
    public string DisplayName { get; }

    /// <summary>Gets or sets the serialized chord text (DSL).</summary>
    public string ChordText
    {
        get => this.chordText;
        set => this.SetField(ref this.chordText, value);
    }

    /// <summary>Gets or sets a value indicating whether this row differs from the default.</summary>
    public bool IsCustomized
    {
        get => this.isCustomized;
        set => this.SetField(ref this.isCustomized, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the button is in
    /// record mode (next KeyDown captures the chord).
    /// </summary>
    public bool IsRecording
    {
        get => this.isRecording;
        set => this.SetField(ref this.isRecording, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
