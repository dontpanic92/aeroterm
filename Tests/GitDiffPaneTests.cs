// <copyright file="GitDiffPaneTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.IO;
using System.Threading.Tasks;
using AeroTerm.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;

/// <summary>
/// Headless tests for the side-by-side Git diff pane.
/// </summary>
[TestFixture]
public sealed class GitDiffPaneTests
{
    /// <summary>
    /// Constructing the pane and refreshing outside a repository should not throw.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_OutsideRepository_DoesNotThrow()
    {
        var pane = new GitDiffPane(() => Path.GetTempPath());

        await pane.RefreshAsync();

        Assert.That(pane.Content, Is.Not.Null);
    }

    /// <summary>
    /// Refreshing with a null working directory should not throw.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_NullWorkingDirectory_DoesNotThrow()
    {
        var pane = new GitDiffPane(() => null);

        Assert.DoesNotThrowAsync(async () => await pane.RefreshAsync());
    }
}
