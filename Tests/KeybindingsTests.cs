// <copyright file="KeybindingsTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using AeroTerm.Services;
using Avalonia.Input;
using NUnit.Framework;

/// <summary>
/// Unit tests for the keybinding model, parser, store, and merge semantics.
/// </summary>
[TestFixture]
public class KeybindingsTests
{
    private string tempDir = string.Empty;

    /// <summary>Create a per-test temp directory under the test work dir.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "keybindings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>Clean up the temp directory after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    /// <summary>Parser should accept a modifier+letter chord using Cmd alias.</summary>
    [Test]
    public void TryParse_CmdShiftT_ReturnsMetaShiftT()
    {
        Assert.That(KeyChordParser.TryParse("Cmd+Shift+T", out var chord), Is.True);
        Assert.That(chord, Is.Not.Null);
        Assert.That(chord!.Modifiers, Is.EqualTo(KeyModifiers.Meta | KeyModifiers.Shift));
        Assert.That(chord.Key, Is.EqualTo(Key.T));
    }

    /// <summary>Parser accepts digit shorthand and normalizes to D-keys.</summary>
    [Test]
    public void TryParse_DigitShorthand_MapsToDKey()
    {
        Assert.That(KeyChordParser.TryParse("Ctrl+1", out var chord), Is.True);
        Assert.That(chord!.Key, Is.EqualTo(Key.D1));
        Assert.That(chord.Modifiers, Is.EqualTo(KeyModifiers.Control));
    }

    /// <summary>Parser accepts named keys like PageDown.</summary>
    [Test]
    public void TryParse_CtrlPageDown_Works()
    {
        Assert.That(KeyChordParser.TryParse("Ctrl+PageDown", out var chord), Is.True);
        Assert.That(chord!.Key, Is.EqualTo(Key.PageDown));
    }

    /// <summary>Parser rejects empty, duplicate-modifier, multi-key input.</summary>
    [Test]
    public void TryParse_InvalidInputs_ReturnFalse()
    {
        Assert.That(KeyChordParser.TryParse(string.Empty, out _), Is.False);
        Assert.That(KeyChordParser.TryParse("   ", out _), Is.False);
        Assert.That(KeyChordParser.TryParse("Ctrl+Ctrl+T", out _), Is.False);
        Assert.That(KeyChordParser.TryParse("Ctrl+T+F", out _), Is.False);
        Assert.That(KeyChordParser.TryParse("Ctrl+Nope", out _), Is.False);
        Assert.That(KeyChordParser.TryParse("Ctrl", out _), Is.False);
    }

    /// <summary>Serialize emits canonical Ctrl+Alt+Shift+Cmd+Key ordering.</summary>
    [Test]
    public void Serialize_EmitsCanonicalModifierOrder()
    {
        var chord = new KeyChord(
            KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt,
            Key.T);
        Assert.That(KeyChordParser.Serialize(chord), Is.EqualTo("Ctrl+Alt+Shift+Cmd+T"));
    }

    /// <summary>Serialize + TryParse round-trip preserves the chord.</summary>
    [Test]
    public void Serialize_TryParse_RoundTrip()
    {
        var original = new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.OemComma);
        var text = KeyChordParser.Serialize(original);
        Assert.That(KeyChordParser.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed!.Modifiers, Is.EqualTo(original.Modifiers));
        Assert.That(parsed.Key, Is.EqualTo(original.Key));
    }

    /// <summary>macOS defaults contain Cmd+T for NewTab.</summary>
    [Test]
    public void BuildDefaults_MacOS_HasCmdT_ForNewTab()
    {
        var set = new KeybindingSet(KeybindingSet.BuildDefaults(isMacOS: true));
        var match = set.Resolve(new KeyChord(KeyModifiers.Meta, Key.T));
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Action, Is.EqualTo(KeybindingAction.NewTab));
    }

    /// <summary>Windows/Linux defaults contain Ctrl+Shift+T for NewTab and Ctrl+PageDown for NextTab.</summary>
    [Test]
    public void BuildDefaults_NonMac_HasCtrlShiftT_AndCtrlPageDown()
    {
        var set = new KeybindingSet(KeybindingSet.BuildDefaults(isMacOS: false));
        var newTab = set.Resolve(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.T));
        Assert.That(newTab?.Action, Is.EqualTo(KeybindingAction.NewTab));

        var nextTab = set.Resolve(new KeyChord(KeyModifiers.Control, Key.PageDown));
        Assert.That(nextTab?.Action, Is.EqualTo(KeybindingAction.NextTab));

        var moveLeft = set.Resolve(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.PageUp));
        Assert.That(moveLeft?.Action, Is.EqualTo(KeybindingAction.MoveTabLeft));

        var moveRight = set.Resolve(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.PageDown));
        Assert.That(moveRight?.Action, Is.EqualTo(KeybindingAction.MoveTabRight));
    }

    /// <summary>macOS defaults map Cmd+Shift+PageUp/PageDown to MoveTabLeft/MoveTabRight.</summary>
    [Test]
    public void BuildDefaults_Mac_HasCmdShiftPageUpDown_ForMoveTab()
    {
        var set = new KeybindingSet(KeybindingSet.BuildDefaults(isMacOS: true));

        var moveLeft = set.Resolve(new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.PageUp));
        Assert.That(moveLeft?.Action, Is.EqualTo(KeybindingAction.MoveTabLeft));

        var moveRight = set.Resolve(new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, Key.PageDown));
        Assert.That(moveRight?.Action, Is.EqualTo(KeybindingAction.MoveTabRight));
    }

    /// <summary>Merge drops all defaults for any overridden action but keeps others intact.</summary>
    [Test]
    public void Merge_OverrideDropsDefaultsPerAction_KeepsOthers()
    {
        var set = new KeybindingSet(KeybindingSet.BuildDefaults(isMacOS: false));
        var overrides = new[]
        {
            new Keybinding(KeybindingAction.NextTab, new KeyChord(KeyModifiers.Alt, Key.Right)),
        };
        var merged = set.Merge(overrides);

        // Original Ctrl+PageDown (default for NextTab) should no longer resolve to NextTab.
        var original = merged.Resolve(new KeyChord(KeyModifiers.Control, Key.PageDown));
        Assert.That(original, Is.Null);

        // New chord resolves.
        var newChord = merged.Resolve(new KeyChord(KeyModifiers.Alt, Key.Right));
        Assert.That(newChord?.Action, Is.EqualTo(KeybindingAction.NextTab));

        // Untouched action (Copy) still resolves to its default.
        var copy = merged.Resolve(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.C));
        Assert.That(copy?.Action, Is.EqualTo(KeybindingAction.Copy));
    }

    /// <summary>Load returns defaults when keybindings.json does not exist.</summary>
    [Test]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new KeybindingStore(this.tempDir);
        var set = store.Load();

        // Sanity check — should be non-empty and match Defaults' binding count.
        Assert.That(set.Bindings.Count, Is.EqualTo(KeybindingSet.Defaults.Bindings.Count));
    }

    /// <summary>Load returns defaults for malformed JSON (no throw).</summary>
    [Test]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var store = new KeybindingStore(this.tempDir);
        File.WriteAllText(store.FilePath, "{not valid json");

        var set = store.Load();
        Assert.That(set.Bindings.Count, Is.EqualTo(KeybindingSet.Defaults.Bindings.Count));
    }

    /// <summary>Save then Load round-trips user overrides and applies them over defaults.</summary>
    [Test]
    public void SaveThenLoad_RoundTripsOverrides()
    {
        var store = new KeybindingStore(this.tempDir);
        var overrides = new List<Keybinding>
        {
            new Keybinding(KeybindingAction.NewTab, new KeyChord(KeyModifiers.Alt, Key.N)),
        };
        Assert.That(store.Save(overrides), Is.True);
        Assert.That(File.Exists(store.FilePath), Is.True);

        var reloaded = store.Load();
        var match = reloaded.Resolve(new KeyChord(KeyModifiers.Alt, Key.N));
        Assert.That(match?.Action, Is.EqualTo(KeybindingAction.NewTab));
    }

    /// <summary>Load skips individual invalid entries but keeps valid ones.</summary>
    [Test]
    public void Load_InvalidEntries_AreSkipped()
    {
        var store = new KeybindingStore(this.tempDir);
        var json = """
            {
              "version": 1,
              "bindings": [
                { "action": "NotAnAction", "chord": "Ctrl+Q" },
                { "action": "NewTab", "chord": "Not-a-Chord" },
                { "action": "Copy", "chord": "Alt+Shift+C" }
              ]
            }
            """;
        File.WriteAllText(store.FilePath, json);

        var set = store.Load();

        // Bad entries ignored; Copy override applied.
        var copy = set.Resolve(new KeyChord(KeyModifiers.Alt | KeyModifiers.Shift, Key.C));
        Assert.That(copy?.Action, Is.EqualTo(KeybindingAction.Copy));

        // NewTab was in overrides but chord was invalid → NewTab override dropped,
        // so defaults for NewTab remain. On both platforms NewTab still resolves.
        var mac = set.Resolve(new KeyChord(KeyModifiers.Meta, Key.T));
        var nonMac = set.Resolve(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, Key.T));
        var newTabDefault = mac ?? nonMac;
        Assert.That(newTabDefault?.Action, Is.EqualTo(KeybindingAction.NewTab));
    }
}
