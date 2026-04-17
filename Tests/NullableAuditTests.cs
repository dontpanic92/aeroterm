// <copyright file="NullableAuditTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Verifies that public <see cref="AeroTerm.Pty"/> APIs reject
/// <see langword="null"/> reference arguments with
/// <see cref="ArgumentNullException"/>. These guard-rail tests pin the
/// null-validation contract introduced by the nullable-audit-pty pass so
/// regressions (silent NREs sneaking back in after refactors) are caught
/// at build time.
/// </summary>
public class NullableAuditTests
{
    /// <summary>
    /// <see cref="VtParser(TerminalBuffer, Action{string}, Action{byte[]}?, Func{string}?, Action{string}?)"/>
    /// rejects null <c>buffer</c>.
    /// </summary>
    [Test]
    public void VtParser_Ctor_NullBuffer_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new VtParser(null!, _ => { }));
        Assert.That(ex!.ParamName, Is.EqualTo("buffer"));
    }

    /// <summary>
    /// <see cref="VtParser(TerminalBuffer, Action{string}, Action{byte[]}?, Func{string}?, Action{string}?)"/>
    /// rejects null <c>titleChanged</c>.
    /// </summary>
    [Test]
    public void VtParser_Ctor_NullTitleChanged_Throws()
    {
        var buffer = new TerminalBuffer(80, 24);
        var ex = Assert.Throws<ArgumentNullException>(
            () => new VtParser(buffer, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("titleChanged"));
    }

    /// <summary>
    /// <see cref="TerminalBuffer.SetAnsiPalette(int[])"/> rejects null input
    /// (previously it would NRE on <c>colors.Length</c>).
    /// </summary>
    [Test]
    public void TerminalBuffer_SetAnsiPalette_Null_Throws()
    {
        var buffer = new TerminalBuffer(80, 24);
        var ex = Assert.Throws<ArgumentNullException>(
            () => buffer.SetAnsiPalette(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("colors"));
    }

    /// <summary>
    /// <see cref="PromptMarksRegistry.Add(PromptMark)"/> rejects null
    /// (previously a null entry would NRE during navigation).
    /// </summary>
    [Test]
    public void PromptMarksRegistry_Add_Null_Throws()
    {
        var registry = new PromptMarksRegistry();
        var ex = Assert.Throws<ArgumentNullException>(
            () => registry.Add(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("mark"));
    }

    /// <summary>
    /// <see cref="PtyConnectionFactory.Create(string, string[], IDictionary{string, string}, string, int, int)"/>
    /// rejects each of its reference-type parameters being null.
    /// </summary>
    /// <param name="nullParam">Which parameter to null out.</param>
    [TestCase("app")]
    [TestCase("args")]
    [TestCase("environment")]
    [TestCase("cwd")]
    public void PtyConnectionFactory_Create_NullReference_Throws(string nullParam)
    {
        string app = nullParam == "app" ? null! : "/bin/sh";
        string[] args = nullParam == "args" ? null! : Array.Empty<string>();
        IDictionary<string, string> env = nullParam == "environment"
            ? null!
            : new Dictionary<string, string>();
        string cwd = nullParam == "cwd" ? null! : "/";

        var ex = Assert.Throws<ArgumentNullException>(
            () => PtyConnectionFactory.Create(app, args, env, cwd, 24, 80));
        Assert.That(ex!.ParamName, Is.EqualTo(nullParam));
    }

    /// <summary>
    /// <see cref="DefaultPtyConnectionFactory.Create(string, string[], IDictionary{string, string}, string, int, int)"/>
    /// rejects null arguments, mirroring the static-shim behaviour.
    /// </summary>
    [Test]
    public void DefaultPtyConnectionFactory_Create_NullApp_Throws()
    {
        var factory = DefaultPtyConnectionFactory.Instance;
        var ex = Assert.Throws<ArgumentNullException>(
            () => factory.Create(
                null!,
                Array.Empty<string>(),
                new Dictionary<string, string>(),
                "/",
                24,
                80));
        Assert.That(ex!.ParamName, Is.EqualTo("app"));
    }
}
