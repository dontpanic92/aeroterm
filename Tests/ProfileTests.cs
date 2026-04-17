// <copyright file="ProfileTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for the <see cref="Profile"/> model and its merge helper.
/// </summary>
[TestFixture]
public class ProfileTests
{
    /// <summary>Freshly-constructed profiles have a non-empty GUID id.</summary>
    [Test]
    public void Constructor_AssignsNonEmptyGuidId()
    {
        var a = new Profile();
        var b = new Profile();
        Assert.That(a.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(b.Id, Is.Not.EqualTo(a.Id));
        Assert.That(Guid.TryParseExact(a.Id, "N", out _), Is.True);
    }

    /// <summary>Default profile name is "Default".</summary>
    [Test]
    public void Constructor_DefaultName()
    {
        Assert.That(new Profile().Name, Is.EqualTo("Default"));
    }

    /// <summary>Profile command overrides fallback command.</summary>
    [Test]
    public void BuildLaunchSpec_ProfileCommandWins()
    {
        var fallback = MakeFallback();
        var p = new Profile { Command = "/bin/zsh" };

        var spec = InvokeBuild(p, fallback);
        Assert.That(spec.Command, Is.EqualTo("/bin/zsh"));
    }

    /// <summary>Null profile command falls back to the baseline command.</summary>
    [Test]
    public void BuildLaunchSpec_NullCommandFallsBack()
    {
        var fallback = MakeFallback();
        var p = new Profile();

        var spec = InvokeBuild(p, fallback);
        Assert.That(spec.Command, Is.EqualTo(fallback.Command));
    }

    /// <summary>Profile args win over fallback args when set.</summary>
    [Test]
    public void BuildLaunchSpec_ArgsPrecedence()
    {
        var fallback = MakeFallback();
        var p = new Profile { Args = new[] { "--login", "-i" } };

        var spec = InvokeBuild(p, fallback);
        Assert.That(spec.Args, Is.EquivalentTo(new[] { "--login", "-i" }));

        var p2 = new Profile();
        var spec2 = InvokeBuild(p2, fallback);
        Assert.That(spec2.Args, Is.EquivalentTo(fallback.Args));
    }

    /// <summary>Profile cwd overrides fallback cwd when non-null/non-empty.</summary>
    [Test]
    public void BuildLaunchSpec_CwdOverride()
    {
        var fallback = MakeFallback();
        var p = new Profile { WorkingDirectory = "/tmp/override" };

        var spec = InvokeBuild(p, fallback);
        Assert.That(spec.Cwd, Is.EqualTo("/tmp/override"));
    }

    /// <summary>Environment overrides are layered on top of the fallback env.</summary>
    [Test]
    public void BuildLaunchSpec_EnvMergesWithProfileWinningCollisions()
    {
        var fallback = new LaunchSpec(
            "/home",
            "/bin/sh",
            Array.Empty<string>(),
            new Dictionary<string, string>
            {
                ["PATH"] = "/usr/bin",
                ["TERM"] = "xterm-256color",
            });

        var p = new Profile
        {
            EnvironmentOverrides = new Dictionary<string, string>
            {
                ["PATH"] = "/custom/bin",
                ["LANG"] = "en_US.UTF-8",
            },
        };

        var spec = InvokeBuild(p, fallback);
        Assert.That(spec.Env["PATH"], Is.EqualTo("/custom/bin"));
        Assert.That(spec.Env["TERM"], Is.EqualTo("xterm-256color"));
        Assert.That(spec.Env["LANG"], Is.EqualTo("en_US.UTF-8"));
    }

    private static LaunchSpec MakeFallback() => new(
        "/home/test",
        "/bin/sh",
        new[] { "-l" },
        new Dictionary<string, string> { ["TERM"] = "xterm-256color" });

    private static LaunchSpec InvokeBuild(Profile p, LaunchSpec fallback)
    {
        // BuildLaunchSpec is internal; InternalsVisibleTo grants access.
        var method = typeof(ProfileStore).GetMethod(
            "BuildLaunchSpec",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (LaunchSpec)method!.Invoke(null, new object[] { p, fallback })!;
    }
}
