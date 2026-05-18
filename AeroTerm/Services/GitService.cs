// <copyright file="GitService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Runs Git CLI commands for the Workbench Git view.
/// </summary>
internal sealed class GitService
{
    /// <summary>
    /// Gets the current repository status for a working directory.
    /// </summary>
    /// <param name="workingDirectory">Terminal working directory.</param>
    /// <returns>Repository status, including an empty state outside repositories.</returns>
    internal async Task<GitRepositoryStatus> GetStatusAsync(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return EmptyStatus(workingDirectory ?? string.Empty, "No terminal working directory is available yet.");
        }

        var rootResult = await this.RunGitAsync(workingDirectory, "rev-parse", "--show-toplevel").ConfigureAwait(true);
        if (!rootResult.Succeeded)
        {
            return EmptyStatus(workingDirectory, "The terminal working directory is not inside a Git repository.");
        }

        var root = rootResult.Output.Trim();
        var statusResult = await this.RunGitAsync(root, "status", "--porcelain=v2", "--branch").ConfigureAwait(true);
        if (!statusResult.Succeeded)
        {
            return EmptyStatus(workingDirectory, statusResult.ErrorMessage);
        }

        return this.ParseStatus(workingDirectory, root, statusResult.Output);
    }

    /// <summary>
    /// Stages a path.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="path">Repository-relative path.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> StageAsync(string repositoryRoot, string path)
    {
        return this.RunGitAsync(repositoryRoot, "add", "--", path);
    }

    /// <summary>
    /// Unstages a path.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="path">Repository-relative path.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> UnstageAsync(string repositoryRoot, string path)
    {
        return this.RunGitAsync(repositoryRoot, "restore", "--staged", "--", path);
    }

    /// <summary>
    /// Discards unstaged changes for a path.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="path">Repository-relative path.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> DiscardAsync(string repositoryRoot, string path)
    {
        return this.RunGitAsync(repositoryRoot, "restore", "--", path);
    }

    /// <summary>
    /// Commits staged changes.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="message">Commit message.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> CommitAsync(string repositoryRoot, string message)
    {
        return this.RunGitAsync(repositoryRoot, "commit", "-m", message);
    }

    /// <summary>
    /// Fetches remote updates.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> FetchAsync(string repositoryRoot)
    {
        return this.RunGitAsync(repositoryRoot, "fetch");
    }

    /// <summary>
    /// Pulls from the configured upstream.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> PullAsync(string repositoryRoot)
    {
        return this.RunGitAsync(repositoryRoot, "pull");
    }

    /// <summary>
    /// Pushes to the configured upstream.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> PushAsync(string repositoryRoot)
    {
        return this.RunGitAsync(repositoryRoot, "push");
    }

    /// <summary>
    /// Gets a diff for a status entry.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="status">Status entry to diff.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> GetDiffAsync(string repositoryRoot, GitFileStatus status)
    {
        if (status.Bucket == GitStatusBucket.Staged)
        {
            return this.RunGitAsync(repositoryRoot, "diff", "--staged", "--", status.Path);
        }

        return this.RunGitAsync(repositoryRoot, "diff", "--", status.Path);
    }

    /// <summary>
    /// Runs Git with the provided arguments.
    /// </summary>
    /// <param name="workingDirectory">Process working directory.</param>
    /// <param name="arguments">Git arguments.</param>
    /// <returns>The Git command result.</returns>
    internal async Task<GitCommandResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitCommandResult(-1, string.Empty, "Unable to start git.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(true);
            var output = await outputTask.ConfigureAwait(true);
            var error = await errorTask.ConfigureAwait(true);
            return new GitCommandResult(process.ExitCode, output, error);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new GitCommandResult(-1, string.Empty, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new GitCommandResult(-1, string.Empty, ex.Message);
        }
    }

    private static GitRepositoryStatus EmptyStatus(string workingDirectory, string errorMessage)
    {
        return new GitRepositoryStatus(
            workingDirectory,
            null,
            null,
            null,
            0,
            0,
            Array.Empty<GitFileStatus>(),
            Array.Empty<GitFileStatus>(),
            Array.Empty<GitFileStatus>(),
            errorMessage);
    }

    private GitRepositoryStatus ParseStatus(string workingDirectory, string root, string output)
    {
        string? branch = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var staged = new List<GitFileStatus>();
        var unstaged = new List<GitFileStatus>();
        var untracked = new List<GitFileStatus>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = line["# branch.head ".Length..];
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = line["# branch.upstream ".Length..];
                continue;
            }

            if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                this.ParseAheadBehind(line, out ahead, out behind);
                continue;
            }

            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked.Add(new GitFileStatus(line[2..], '?', '?', GitStatusBucket.Untracked));
                continue;
            }

            if (line.StartsWith("1 ", StringComparison.Ordinal) && line.Length > 10)
            {
                var indexStatus = line[2];
                var workTreeStatus = line[3];
                var path = this.GetPorcelainPath(line);
                if (indexStatus != '.')
                {
                    staged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Staged));
                }

                if (workTreeStatus != '.')
                {
                    unstaged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Unstaged));
                }
            }
            else if (line.StartsWith("2 ", StringComparison.Ordinal) && line.Length > 10)
            {
                var indexStatus = line[2];
                var workTreeStatus = line[3];
                var path = this.GetRenamedPorcelainPath(line);
                staged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Staged));
                if (workTreeStatus != '.')
                {
                    unstaged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Unstaged));
                }
            }
            else if (line.StartsWith("u ", StringComparison.Ordinal) && line.Length > 10)
            {
                var path = this.GetPorcelainPath(line);
                unstaged.Add(new GitFileStatus(path, 'U', 'U', GitStatusBucket.Unstaged));
            }
        }

        return new GitRepositoryStatus(
            workingDirectory,
            root,
            branch,
            upstream,
            ahead,
            behind,
            staged,
            unstaged,
            untracked,
            null);
    }

    private void ParseAheadBehind(string line, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;
        foreach (var part in line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 2)
            {
                continue;
            }

            if (part[0] == '+' &&
                int.TryParse(part[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAhead))
            {
                ahead = parsedAhead;
            }
            else if (part[0] == '-' &&
                int.TryParse(part[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBehind))
            {
                behind = parsedBehind;
            }
        }
    }

    private string GetPorcelainPath(string line)
    {
        var index = this.IndexOfNthSpace(line, 8);
        if (index < 0 || index + 1 >= line.Length)
        {
            return line;
        }

        return line[(index + 1)..];
    }

    private string GetRenamedPorcelainPath(string line)
    {
        var index = this.IndexOfNthSpace(line, 9);
        if (index < 0 || index + 1 >= line.Length)
        {
            return line;
        }

        var path = line[(index + 1)..];
        var separator = path.IndexOf('\t', StringComparison.Ordinal);
        return separator >= 0 ? path[..separator] : path;
    }

    private int IndexOfNthSpace(string value, int count)
    {
        var found = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == ' ')
            {
                found++;
                if (found == count)
                {
                    return index;
                }
            }
        }

        return -1;
    }
}
