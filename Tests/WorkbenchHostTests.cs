// <copyright file="WorkbenchHostTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;

/// <summary>
/// Headless tests for the experimental Workbench host.
/// </summary>
[TestFixture]
public sealed class WorkbenchHostTests
{
    /// <summary>
    /// Re-enabling Workbench while the Explorer section is already built should not re-parent reused controls.
    /// </summary>
    [AvaloniaTest]
    public void SetWorkbenchEnabled_WhenExplorerAlreadyShown_DoesNotReparentControls()
    {
        var host = new WorkbenchHost(new Border());

        Assert.DoesNotThrow(() =>
        {
            host.SetWorkbenchEnabled(true);
            host.SetWorkbenchEnabled(false);
            host.SetWorkbenchEnabled(true);
        });
    }

    /// <summary>
    /// Switching sections repeatedly should reuse each cached view without inserting controls into multiple parents.
    /// </summary>
    [AvaloniaTest]
    public void ShowSections_Repeatedly_DoesNotReparentControls()
    {
        var host = new WorkbenchHost(new Border());
        host.SetWorkbenchEnabled(true);

        Assert.DoesNotThrow(() =>
        {
            host.ShowExplorer();
            host.ShowEditor();
            host.ShowExplorer();
            host.ShowGit();
            host.ShowEditor();
            host.ShowGit();
        });
    }
}
