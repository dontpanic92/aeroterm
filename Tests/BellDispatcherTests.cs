// <copyright file="BellDispatcherTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Collections.Generic;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="BellDispatcher"/>, the pure mapping from a
/// <see cref="BellAction"/> to <see cref="IBellOutputs"/> calls.
/// </summary>
[TestFixture]
public class BellDispatcherTests
{
    /// <summary>
    /// <see cref="BellAction.None"/> invokes nothing.
    /// </summary>
    [Test]
    public void Dispatch_None_InvokesNothing()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.None, fake);
        Assert.That(fake.Calls, Is.Empty);
    }

    /// <summary>
    /// <see cref="BellAction.Visual"/> invokes only <c>Visual</c>.
    /// </summary>
    [Test]
    public void Dispatch_Visual_InvokesVisualOnly()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.Visual, fake);
        Assert.That(fake.Calls, Is.EqualTo(new[] { "Visual" }));
    }

    /// <summary>
    /// <see cref="BellAction.Audio"/> invokes only <c>Audio</c>.
    /// </summary>
    [Test]
    public void Dispatch_Audio_InvokesAudioOnly()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.Audio, fake);
        Assert.That(fake.Calls, Is.EqualTo(new[] { "Audio" }));
    }

    /// <summary>
    /// <see cref="BellAction.Notification"/> invokes only <c>Notify</c>.
    /// </summary>
    [Test]
    public void Dispatch_Notification_InvokesNotifyOnly()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.Notification, fake);
        Assert.That(fake.Calls, Is.EqualTo(new[] { "Notify" }));
    }

    /// <summary>
    /// <see cref="BellAction.VisualAndAudio"/> invokes visual and audio but
    /// not notify.
    /// </summary>
    [Test]
    public void Dispatch_VisualAndAudio_InvokesVisualAndAudio()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.VisualAndAudio, fake);
        Assert.That(fake.Calls, Is.EqualTo(new[] { "Visual", "Audio" }));
    }

    /// <summary>
    /// <see cref="BellAction.All"/> invokes all three outputs in order.
    /// </summary>
    [Test]
    public void Dispatch_All_InvokesAllThreeInOrder()
    {
        var fake = new FakeBellOutputs();
        BellDispatcher.Dispatch(BellAction.All, fake);
        Assert.That(fake.Calls, Is.EqualTo(new[] { "Visual", "Audio", "Notify" }));
    }

    private sealed class FakeBellOutputs : IBellOutputs
    {
        public List<string> Calls { get; } = new();

        public void Visual() => this.Calls.Add("Visual");

        public void Audio() => this.Calls.Add("Audio");

        public void Notify() => this.Calls.Add("Notify");
    }
}
