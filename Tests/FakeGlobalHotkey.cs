// <copyright file="FakeGlobalHotkey.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using AeroTerm.Services;

/// <summary>
/// Test double for <see cref="IGlobalHotkeySource"/>. Records every
/// registration attempt and lets tests synthesize hotkey firings without
/// touching any real keyboard interop.
/// </summary>
public sealed class FakeGlobalHotkey : IGlobalHotkeySource
{
    private readonly Dictionary<KeyChord, Action> active = new();

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="IsSupported"/>
    /// should report the platform as supporting global hotkeys.
    /// </summary>
    public bool Supported { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether every subsequent
    /// <see cref="TryRegister"/> call should fail (to simulate a chord
    /// already owned by another process).
    /// </summary>
    public bool FailRegistration { get; set; }

    /// <summary>
    /// Gets the chords that have been registered since construction and
    /// not yet unregistered.
    /// </summary>
    public IReadOnlyCollection<KeyChord> Registered => this.active.Keys;

    /// <summary>
    /// Gets the number of times <see cref="TryRegister"/> succeeded.
    /// </summary>
    public int RegisterSuccesses { get; private set; }

    /// <summary>
    /// Gets the number of disposes observed for handed-out registrations.
    /// </summary>
    public int UnregisterCount { get; private set; }

    /// <inheritdoc />
    public bool IsSupported => this.Supported;

    /// <inheritdoc />
    public bool TryRegister(KeyChord chord, Action handler, out IDisposable? registration)
    {
        registration = null;
        if (!this.Supported || this.FailRegistration || this.active.ContainsKey(chord))
        {
            return false;
        }

        this.active[chord] = handler;
        this.RegisterSuccesses++;
        registration = new Registration(this, chord);
        return true;
    }

    /// <summary>
    /// Synthesizes a hotkey firing for the given chord. Throws if nothing
    /// is registered on that chord.
    /// </summary>
    /// <param name="chord">The chord to fire.</param>
    public void Fire(KeyChord chord)
    {
        if (!this.active.TryGetValue(chord, out var h))
        {
            throw new InvalidOperationException($"No registration for {chord}.");
        }

        h();
    }

    private sealed class Registration : IDisposable
    {
        private readonly FakeGlobalHotkey owner;
        private readonly KeyChord chord;
        private bool disposed;

        public Registration(FakeGlobalHotkey owner, KeyChord chord)
        {
            this.owner = owner;
            this.chord = chord;
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.owner.active.Remove(this.chord);
            this.owner.UnregisterCount++;
        }
    }
}
