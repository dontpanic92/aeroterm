// <copyright file="FileLoggerTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

/// <summary>
/// Tests process-specific behavior for <see cref="FileLogger"/>.
/// </summary>
[TestFixture]
public class FileLoggerTests
{
    /// <summary>
    /// Each AeroTerm process writes to a process-specific file so concurrent
    /// instances cannot suppress or interleave each other's diagnostics.
    /// </summary>
    [Test]
    public void Constructor_UsesProcessSpecificLog()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AeroTermTests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            using var logger = new FileLogger(directory);
            Assert.That(
                Path.GetFileName(logger.LogFilePath),
                Is.EqualTo($"aeroterm-{Environment.ProcessId}.log"));

            logger.LogInformation("process diagnostic");
            logger.Dispose();

            Assert.That(File.ReadAllText(logger.LogFilePath), Does.Contain("process diagnostic"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
