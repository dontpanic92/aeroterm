// <copyright file="TerminalSessionCoordinatorCwdTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using System.Reflection;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="TerminalSessionCoordinator"/> working-directory
/// resolution, in particular the per-session cwd side-channel file used for
/// shells (e.g. Windows PowerShell) whose <c>cd</c> is otherwise invisible.
/// </summary>
[TestFixture]
public sealed class TerminalSessionCoordinatorCwdTests
{
    private static readonly FieldInfo CwdFilePathField =
        typeof(TerminalSessionCoordinator).GetField("cwdFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo ShellReportedCwdField =
        typeof(TerminalSessionCoordinator).GetField("shellReportedCwd", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private string tempFile = string.Empty;

    /// <summary>Creates a unique temp file path for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempFile = Path.Combine(Path.GetTempPath(), "cwd-coord-test-" + Guid.NewGuid().ToString("N") + ".txt");
    }

    /// <summary>Removes the temp file after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            File.Delete(this.tempFile);
        }
        catch (IOException)
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// The cwd side-channel file is read (and trimmed) when no escape-sequence
    /// cwd has been reported, so a PowerShell <c>cd</c> recorded only in the
    /// file is surfaced.
    /// </summary>
    [Test]
    public void TryGetCurrentWorkingDirectory_ReadsCwdFile_WhenNoOscReported()
    {
        File.WriteAllText(this.tempFile, "D:\\Projects\\aeroterm\r\n");
        using var coordinator = new TerminalSessionCoordinator(new AppSettings());
        CwdFilePathField.SetValue(coordinator, this.tempFile);

        Assert.That(coordinator.TryGetCurrentWorkingDirectory(), Is.EqualTo("D:\\Projects\\aeroterm"));
    }

    /// <summary>
    /// An escape-sequence reported cwd takes precedence over the file (it is
    /// the proactive signal that also fires change events).
    /// </summary>
    [Test]
    public void TryGetCurrentWorkingDirectory_PrefersShellReportedCwd_OverFile()
    {
        File.WriteAllText(this.tempFile, "D:\\from-file");
        using var coordinator = new TerminalSessionCoordinator(new AppSettings());
        CwdFilePathField.SetValue(coordinator, this.tempFile);
        ShellReportedCwdField.SetValue(coordinator, "D:\\from-osc");

        Assert.That(coordinator.TryGetCurrentWorkingDirectory(), Is.EqualTo("D:\\from-osc"));
    }

    /// <summary>
    /// A missing or empty cwd file is ignored so resolution falls through
    /// instead of returning an empty path read from the file.
    /// </summary>
    [Test]
    public void TryGetCurrentWorkingDirectory_IgnoresMissingOrEmptyFile()
    {
        using var coordinator = new TerminalSessionCoordinator(new AppSettings());
        CwdFilePathField.SetValue(coordinator, this.tempFile); // file does not exist

        // No launch spec (Initialize not called), so a clean fall-through yields null.
        Assert.That(coordinator.TryGetCurrentWorkingDirectory(), Is.Null);

        File.WriteAllText(this.tempFile, "   \r\n");
        Assert.That(coordinator.TryGetCurrentWorkingDirectory(), Is.Null);
    }
}
