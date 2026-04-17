// <copyright file="PrimarySelectionTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for PRIMARY-selection plumbing and middle-click paste
/// dispatch.
/// </summary>
public class PrimarySelectionTests
{
    /// <summary>
    /// The shared <see cref="DefaultPrimarySelectionService"/> must report
    /// <c>IsAvailable == false</c> on macOS and Windows — these platforms
    /// don't have an X11 PRIMARY selection. This also transitively proves
    /// that <see cref="MiddleClickPaster.TryWritePrimaryAsync"/> short
    /// circuits without invoking an external helper.
    /// </summary>
    [Test]
    public void DefaultService_IsNotAvailable_OnNonLinux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Ignore("Linux hosts may have xclip/xsel on PATH; this assertion only applies to non-Linux.");
            return;
        }

        Assert.That(DefaultPrimarySelectionService.Instance.IsAvailable, Is.False);
    }

    /// <summary>
    /// <see cref="MiddleClickPaster.TryWritePrimaryAsync"/> must not invoke
    /// the service when <see cref="IPrimarySelectionService.IsAvailable"/>
    /// is false, matching the non-Linux / Wayland fallback path.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryWritePrimary_SkipsBackend_WhenServiceUnavailable()
    {
        var fake = new FakePrimaryService { IsAvailable = false };

        bool dispatched = await MiddleClickPaster.TryWritePrimaryAsync(fake, "hello");

        Assert.That(dispatched, Is.False);
        Assert.That(fake.Writes, Is.Empty);
    }

    /// <summary>
    /// When the service reports available, the write is forwarded.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryWritePrimary_ForwardsWrite_WhenAvailable()
    {
        var fake = new FakePrimaryService { IsAvailable = true };

        bool dispatched = await MiddleClickPaster.TryWritePrimaryAsync(fake, "hello");

        Assert.That(dispatched, Is.True);
        Assert.That(fake.Writes, Is.EquivalentTo(new[] { "hello" }));
    }

    /// <summary>
    /// <see cref="MiddleClickPaster.TryPasteAsync"/> must not invoke the
    /// PRIMARY service, the clipboard fallback, or the paste target when
    /// <c>middleClickPastes=false</c>. Covers the opt-out acceptance
    /// criterion.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryPaste_NoOp_WhenDisabled()
    {
        var fake = new FakePrimaryService { IsAvailable = true, ReadValue = "primary-content" };
        int fallbackCalls = 0;
        string? pasted = null;

        bool dispatched = await MiddleClickPaster.TryPasteAsync(
            middleClickPastes: false,
            primary: fake,
            clipboardFallback: () =>
            {
                fallbackCalls++;
                return Task.FromResult<string?>("clipboard-content");
            },
            onText: t => pasted = t);

        Assert.That(dispatched, Is.False);
        Assert.That(pasted, Is.Null);
        Assert.That(fake.Reads, Is.Zero);
        Assert.That(fallbackCalls, Is.Zero);
    }

    /// <summary>
    /// When PRIMARY returns text, the clipboard fallback is never queried.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryPaste_UsesPrimary_WhenPrimaryHasText()
    {
        var fake = new FakePrimaryService { IsAvailable = true, ReadValue = "primary-content" };
        int fallbackCalls = 0;
        string? pasted = null;

        bool dispatched = await MiddleClickPaster.TryPasteAsync(
            middleClickPastes: true,
            primary: fake,
            clipboardFallback: () =>
            {
                fallbackCalls++;
                return Task.FromResult<string?>("clipboard-content");
            },
            onText: t => pasted = t);

        Assert.That(dispatched, Is.True);
        Assert.That(pasted, Is.EqualTo("primary-content"));
        Assert.That(fallbackCalls, Is.Zero);
    }

    /// <summary>
    /// When PRIMARY is empty or unavailable, <see cref="MiddleClickPaster.TryPasteAsync"/>
    /// falls through to the clipboard — this is the macOS/Windows path and
    /// also the Linux/Wayland fallback.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryPaste_FallsBackToClipboard_WhenPrimaryEmpty()
    {
        var fake = new FakePrimaryService { IsAvailable = false, ReadValue = null };
        string? pasted = null;

        bool dispatched = await MiddleClickPaster.TryPasteAsync(
            middleClickPastes: true,
            primary: fake,
            clipboardFallback: () => Task.FromResult<string?>("clipboard-content"),
            onText: t => pasted = t);

        Assert.That(dispatched, Is.True);
        Assert.That(pasted, Is.EqualTo("clipboard-content"));
    }

    /// <summary>
    /// Both sources empty → no paste is dispatched.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task TryPaste_NoOp_WhenAllSourcesEmpty()
    {
        var fake = new FakePrimaryService { IsAvailable = true, ReadValue = null };
        string? pasted = null;

        bool dispatched = await MiddleClickPaster.TryPasteAsync(
            middleClickPastes: true,
            primary: fake,
            clipboardFallback: () => Task.FromResult<string?>(null),
            onText: t => pasted = t);

        Assert.That(dispatched, Is.False);
        Assert.That(pasted, Is.Null);
    }

    private sealed class FakePrimaryService : IPrimarySelectionService
    {
        public bool IsAvailable { get; set; }

        public string? ReadValue { get; set; }

        public int Reads { get; private set; }

        public System.Collections.Generic.List<string> Writes { get; } = new();

        public Task<string?> ReadAsync()
        {
            this.Reads++;
            return Task.FromResult(this.ReadValue);
        }

        public Task WriteAsync(string text)
        {
            this.Writes.Add(text);
            return Task.CompletedTask;
        }
    }
}
