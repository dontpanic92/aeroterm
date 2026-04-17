// <copyright file="Program.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>
/// Entry point for the AeroTerm benchmark suite. Run with
/// <c>dotnet run -c Release --project AeroTerm.Benchmarks</c>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Dispatches a BenchmarkDotNet run from the CLI arguments.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to BenchmarkSwitcher.</param>
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
