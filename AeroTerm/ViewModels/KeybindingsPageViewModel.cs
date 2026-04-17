// <copyright file="KeybindingsPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AeroTerm.Services;
using Avalonia.Input;

/// <summary>
/// View model for the Keybindings settings page. Shows one row per
/// <see cref="KeybindingAction"/>, lets the user capture a new chord,
/// reset to default, and persists changes through
/// <see cref="KeybindingStore"/>.
/// </summary>
internal sealed class KeybindingsPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly KeybindingStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingsPageViewModel"/> class.
    /// </summary>
    /// <param name="store">The keybinding store to persist through.</param>
    public KeybindingsPageViewModel(KeybindingStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.Rows = new ObservableCollection<KeybindingRow>();
        this.Rebuild();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Keybindings";

    /// <summary>
    /// Gets the visible rows, one per action. Excludes reserved actions.
    /// </summary>
    public ObservableCollection<KeybindingRow> Rows { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels =>
        this.Rows.Select(r => r.DisplayName).ToArray();

    /// <summary>
    /// Replaces a row's chord with the one the user just pressed. The
    /// change is persisted via <see cref="KeybindingStore.Save"/> and
    /// <see cref="App.ReloadKeybindings"/> is called so the new binding
    /// takes effect immediately across the app.
    /// </summary>
    /// <param name="row">The row being edited.</param>
    /// <param name="chord">The captured chord.</param>
    public void CaptureChord(KeybindingRow row, KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(chord);
        row.ChordText = KeyChordParser.Serialize(chord);
        row.IsCustomized = true;
        this.PersistAndReload();
    }

    /// <summary>
    /// Resets a single row to the platform default (clears the override).
    /// </summary>
    /// <param name="row">The row to reset.</param>
    public void ResetRow(KeybindingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.IsCustomized = false;
        row.ChordText = DefaultChordText(row.Action);
        this.PersistAndReload();
    }

    /// <summary>
    /// Resets every row to defaults and removes the user's <c>keybindings.json</c>
    /// overrides.
    /// </summary>
    public void ResetAll()
    {
        foreach (var row in this.Rows)
        {
            row.IsCustomized = false;
            row.ChordText = DefaultChordText(row.Action);
        }

        this.PersistAndReload();
    }

    private static string DefaultChordText(KeybindingAction action)
    {
        foreach (var b in KeybindingSet.Defaults.Bindings)
        {
            if (b.Action == action)
            {
                return KeyChordParser.Serialize(b.Chord);
            }
        }

        return string.Empty;
    }

    private void Rebuild()
    {
        this.Rows.Clear();

        // Load current effective set so existing overrides show up.
        var current = this.store.Load();
        var currentByAction = new Dictionary<KeybindingAction, Keybinding>();
        foreach (var b in current.Bindings)
        {
            currentByAction[b.Action] = b;
        }

        var defaultsByAction = new Dictionary<KeybindingAction, Keybinding>();
        foreach (var b in KeybindingSet.Defaults.Bindings)
        {
            defaultsByAction[b.Action] = b;
        }

        foreach (KeybindingAction action in Enum.GetValues<KeybindingAction>())
        {
            // Reserved actions aren't wired into any dispatcher yet.
            if (action is KeybindingAction.ToggleTransparency or KeybindingAction.OpenCommandPalette)
            {
                continue;
            }

            currentByAction.TryGetValue(action, out var current2);
            defaultsByAction.TryGetValue(action, out var defaultBinding);

            var chordText = current2 is not null
                ? KeyChordParser.Serialize(current2.Chord)
                : defaultBinding is not null
                    ? KeyChordParser.Serialize(defaultBinding.Chord)
                    : string.Empty;

            var isCustomized = current2 is not null
                && (defaultBinding is null
                    || current2.Chord.Modifiers != defaultBinding.Chord.Modifiers
                    || current2.Chord.Key != defaultBinding.Chord.Key);

            this.Rows.Add(new KeybindingRow(action, KeybindingSet.GetDisplayName(action))
            {
                ChordText = chordText,
                IsCustomized = isCustomized,
            });
        }
    }

    private void PersistAndReload()
    {
        // Build the overrides list: every row whose chord differs from the
        // default chord OR whose action had no default.
        var defaultsByAction = new Dictionary<KeybindingAction, Keybinding>();
        foreach (var b in KeybindingSet.Defaults.Bindings)
        {
            defaultsByAction[b.Action] = b;
        }

        var overrides = new List<Keybinding>();
        foreach (var row in this.Rows)
        {
            if (string.IsNullOrEmpty(row.ChordText))
            {
                continue;
            }

            if (!KeyChordParser.TryParse(row.ChordText, out var chord) || chord is null)
            {
                continue;
            }

            defaultsByAction.TryGetValue(row.Action, out var def);
            var isDefault = def is not null
                && def.Chord.Modifiers == chord.Modifiers
                && def.Chord.Key == chord.Key;
            if (!isDefault)
            {
                overrides.Add(new Keybinding(row.Action, chord));
            }
        }

        this.store.Save(overrides);
        App.ReloadKeybindings();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
