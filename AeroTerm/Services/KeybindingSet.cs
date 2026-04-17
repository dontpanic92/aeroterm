// <copyright file="KeybindingSet.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Input;

/// <summary>
/// An immutable, ordered list of <see cref="Keybinding"/> entries plus
/// lookup (<see cref="Resolve"/>) and override-merge (<see cref="Merge"/>)
/// helpers.
/// </summary>
public sealed class KeybindingSet
{
    private readonly List<Keybinding> bindings;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingSet"/> class.
    /// </summary>
    /// <param name="bindings">The bindings, in match order.</param>
    public KeybindingSet(IEnumerable<Keybinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = new List<Keybinding>(bindings);
    }

    /// <summary>
    /// Gets the platform-appropriate default binding set for the running
    /// operating system.
    /// </summary>
    public static KeybindingSet Defaults { get; } =
        new KeybindingSet(BuildDefaults(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));

    /// <summary>
    /// Gets the ordered binding list. First match wins in <see cref="Resolve"/>.
    /// </summary>
    public IReadOnlyList<Keybinding> Bindings => this.bindings;

    /// <summary>
    /// Builds the canonical default binding list for the given OS. Exposed
    /// so tests can compare macOS-vs-non-macOS defaults without a runtime
    /// host.
    /// </summary>
    /// <param name="isMacOS">Whether macOS defaults should be used.</param>
    /// <returns>An ordered list of defaults.</returns>
    public static IReadOnlyList<Keybinding> BuildDefaults(bool isMacOS)
    {
        var list = new List<Keybinding>();

        if (isMacOS)
        {
            list.Add(new Keybinding(KeybindingAction.NewTab, new KeyChord(KeyModifiers.Meta, Key.T)));
            list.Add(new Keybinding(KeybindingAction.CloseTab, new KeyChord(KeyModifiers.Meta, Key.W)));
            list.Add(new Keybinding(KeybindingAction.DuplicateTab, new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.D)));
            list.Add(new Keybinding(KeybindingAction.NextTab, new KeyChord(KeyModifiers.Control, Key.Tab)));
            list.Add(new Keybinding(KeybindingAction.PreviousTab, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.Tab)));
            for (int i = 0; i < 9; i++)
            {
                list.Add(new Keybinding(
                    (KeybindingAction)((int)KeybindingAction.JumpToTab1 + i),
                    new KeyChord(KeyModifiers.Meta, (Key)((int)Key.D1 + i))));
            }

            list.Add(new Keybinding(KeybindingAction.Copy, new KeyChord(KeyModifiers.Meta, Key.C)));
            list.Add(new Keybinding(KeybindingAction.Paste, new KeyChord(KeyModifiers.Meta, Key.V)));
            list.Add(new Keybinding(KeybindingAction.FindInScrollback, new KeyChord(KeyModifiers.Meta, Key.F)));
            list.Add(new Keybinding(KeybindingAction.OpenCommandPalette, new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.P)));
            list.Add(new Keybinding(KeybindingAction.MoveTabLeft, new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.PageUp)));
            list.Add(new Keybinding(KeybindingAction.MoveTabRight, new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.PageDown)));
            list.Add(new Keybinding(KeybindingAction.GroupNewFromActive, new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.G)));
            list.Add(new Keybinding(KeybindingAction.UngroupActive, new KeyChord(KeyModifiers.Meta | KeyModifiers.Alt, Key.G)));

            // OpenSettings, NewWindow, CloseWindow: handled by the macOS
            // native menu / OS, so no chord defaults here — avoids
            // double-firing. Users may still bind them via the
            // Keybindings page.
        }
        else
        {
            list.Add(new Keybinding(KeybindingAction.NewTab, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.T)));
            list.Add(new Keybinding(KeybindingAction.CloseTab, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.W)));
            list.Add(new Keybinding(KeybindingAction.DuplicateTab, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.D)));
            list.Add(new Keybinding(KeybindingAction.NextTab, new KeyChord(KeyModifiers.Control, Key.PageDown)));
            list.Add(new Keybinding(KeybindingAction.NextTab, new KeyChord(KeyModifiers.Control, Key.Tab)));
            list.Add(new Keybinding(KeybindingAction.PreviousTab, new KeyChord(KeyModifiers.Control, Key.PageUp)));
            list.Add(new Keybinding(KeybindingAction.PreviousTab, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.Tab)));
            for (int i = 0; i < 9; i++)
            {
                list.Add(new Keybinding(
                    (KeybindingAction)((int)KeybindingAction.JumpToTab1 + i),
                    new KeyChord(KeyModifiers.Control, (Key)((int)Key.D1 + i))));
            }

            list.Add(new Keybinding(KeybindingAction.Copy, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.C)));
            list.Add(new Keybinding(KeybindingAction.Paste, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.V)));
            list.Add(new Keybinding(KeybindingAction.FindInScrollback, new KeyChord(KeyModifiers.Control, Key.F)));
            list.Add(new Keybinding(KeybindingAction.OpenSettings, new KeyChord(KeyModifiers.Control, Key.OemComma)));
            list.Add(new Keybinding(KeybindingAction.OpenCommandPalette, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.P)));
            list.Add(new Keybinding(KeybindingAction.MoveTabLeft, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.PageUp)));
            list.Add(new Keybinding(KeybindingAction.MoveTabRight, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.PageDown)));
            list.Add(new Keybinding(KeybindingAction.GroupNewFromActive, new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.G)));
            list.Add(new Keybinding(KeybindingAction.UngroupActive, new KeyChord(KeyModifiers.Control | KeyModifiers.Alt, Key.G)));
        }

        return list;
    }

    /// <summary>
    /// Returns a user-friendly display name for the given action.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>A human-readable name.</returns>
    public static string GetDisplayName(KeybindingAction action) => action switch
    {
        KeybindingAction.NewTab => "New tab",
        KeybindingAction.CloseTab => "Close tab",
        KeybindingAction.NextTab => "Next tab",
        KeybindingAction.PreviousTab => "Previous tab",
        KeybindingAction.JumpToTab1 => "Jump to tab 1",
        KeybindingAction.JumpToTab2 => "Jump to tab 2",
        KeybindingAction.JumpToTab3 => "Jump to tab 3",
        KeybindingAction.JumpToTab4 => "Jump to tab 4",
        KeybindingAction.JumpToTab5 => "Jump to tab 5",
        KeybindingAction.JumpToTab6 => "Jump to tab 6",
        KeybindingAction.JumpToTab7 => "Jump to tab 7",
        KeybindingAction.JumpToTab8 => "Jump to tab 8",
        KeybindingAction.JumpToTab9 => "Jump to tab 9",
        KeybindingAction.DuplicateTab => "Duplicate tab",
        KeybindingAction.OpenSettings => "Open Settings",
        KeybindingAction.Copy => "Copy",
        KeybindingAction.Paste => "Paste",
        KeybindingAction.FindInScrollback => "Find in scrollback",
        KeybindingAction.NewWindow => "New window",
        KeybindingAction.CloseWindow => "Close window",
        KeybindingAction.ToggleTransparency => "Toggle transparency (reserved)",
        KeybindingAction.OpenCommandPalette => "Open command palette",
        KeybindingAction.MoveTabLeft => "Move tab left",
        KeybindingAction.MoveTabRight => "Move tab right",
        KeybindingAction.GroupNewFromActive => "Group: new group from active tab",
        KeybindingAction.UngroupActive => "Group: ungroup active tab",
        _ => action.ToString(),
    };

    /// <summary>
    /// Finds the first binding whose chord matches the supplied
    /// modifiers+key, or <see langword="null"/> if none does.
    /// </summary>
    /// <param name="chord">The pressed chord.</param>
    /// <returns>The matched binding, or <see langword="null"/>.</returns>
    public Keybinding? Resolve(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        foreach (var b in this.bindings)
        {
            if (b.Chord.Modifiers == chord.Modifiers && b.Chord.Key == chord.Key)
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>
    /// Produces a new set where every action covered by
    /// <paramref name="overrides"/> has its defaults dropped and is
    /// replaced by the overrides (in order). Actions that do not appear in
    /// <paramref name="overrides"/> keep all of their default entries.
    /// </summary>
    /// <param name="overrides">User-supplied overrides.</param>
    /// <returns>A merged <see cref="KeybindingSet"/>.</returns>
    public KeybindingSet Merge(IEnumerable<Keybinding> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var overrideList = overrides.ToList();
        var overridden = new HashSet<KeybindingAction>(overrideList.Select(o => o.Action));

        var merged = new List<Keybinding>();
        foreach (var b in this.bindings)
        {
            if (!overridden.Contains(b.Action))
            {
                merged.Add(b);
            }
        }

        merged.AddRange(overrideList);
        return new KeybindingSet(merged);
    }
}
