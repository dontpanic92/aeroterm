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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Runs Git CLI commands for the Workbench Git view.
/// </summary>
internal sealed class GitService
{
    private readonly TextEditorService textEditorService = new();

    /// <summary>
    /// Creates process start information for a Git command.
    /// </summary>
    /// <param name="workingDirectory">Process working directory.</param>
    /// <param name="arguments">Git arguments.</param>
    /// <returns>Configured process start information.</returns>
    internal static ProcessStartInfo CreateStartInfo(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

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
        var statusResult = await this.RunGitAsync(
            root,
            "status",
            "--porcelain=v2",
            "--branch",
            "--untracked-files=all",
            "-z").ConfigureAwait(true);
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
    /// Stages all repository changes.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> StageAllAsync(string repositoryRoot)
    {
        return this.RunGitAsync(repositoryRoot, "add", "-A");
    }

    /// <summary>
    /// Unstages a path.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="path">Repository-relative path.</param>
    /// <returns>The Git command result.</returns>
    internal async Task<GitCommandResult> UnstageAsync(string repositoryRoot, string path)
    {
        return await this.UnstagePathsAsync(repositoryRoot, new[] { path }).ConfigureAwait(true);
    }

    /// <summary>
    /// Unstages a status entry, including both sides of a rename or copy.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="status">Status entry to unstage.</param>
    /// <returns>The Git command result.</returns>
    internal async Task<GitCommandResult> UnstageAsync(string repositoryRoot, GitFileStatus status)
    {
        var paths = status.OriginalPath is null
            ? new[] { status.Path }
            : new[] { status.Path, status.OriginalPath };
        return await this.UnstagePathsAsync(repositoryRoot, paths).ConfigureAwait(true);
    }

    /// <summary>
    /// Unstages all staged repository changes.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The Git command result.</returns>
    internal async Task<GitCommandResult> UnstageAllAsync(string repositoryRoot)
    {
        var headResult = await this.RunGitAsync(repositoryRoot, "rev-parse", "--verify", "HEAD").ConfigureAwait(true);
        return headResult.Succeeded
            ? await this.RunGitAsync(repositoryRoot, "restore", "--staged", ":/").ConfigureAwait(true)
            : await this.RunGitAsync(repositoryRoot, "rm", "--cached", "-r", "--", ".").ConfigureAwait(true);
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
    /// Deletes an untracked path from the working tree.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="path">Repository-relative path.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> DeleteUntrackedAsync(string repositoryRoot, string path)
    {
        return this.RunGitAsync(repositoryRoot, "clean", "-f", "-d", "--", path);
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
    /// Pulls from the configured upstream and then pushes local commits.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <returns>The first failed command result, or the successful push result.</returns>
    internal async Task<GitCommandResult> SyncAsync(string repositoryRoot)
    {
        var pullResult = await this.PullAsync(repositoryRoot).ConfigureAwait(true);
        return pullResult.Succeeded
            ? await this.PushAsync(repositoryRoot).ConfigureAwait(true)
            : pullResult;
    }

    /// <summary>
    /// Gets a diff for a status entry.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="status">Status entry to diff.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> GetDiffAsync(string repositoryRoot, GitFileStatus status)
    {
        return this.GetDiffAsync(repositoryRoot, status, contextLines: null);
    }

    /// <summary>
    /// Gets a diff for a status entry with an optional context line count.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="status">Status entry to diff.</param>
    /// <param name="contextLines">Number of context lines, or <see langword="null"/> for Git's default.</param>
    /// <returns>The Git command result.</returns>
    internal Task<GitCommandResult> GetDiffAsync(string repositoryRoot, GitFileStatus status, int? contextLines)
    {
        var arguments = new List<string> { "diff" };
        if (status.Bucket == GitStatusBucket.Staged)
        {
            arguments.Add("--staged");
        }

        if (contextLines is not null)
        {
            arguments.Add($"--unified={contextLines.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        arguments.Add("--");
        arguments.Add(status.Path);
        return this.RunGitAsync(repositoryRoot, arguments.ToArray());
    }

    /// <summary>
    /// Loads old and new full-file content for a status entry.
    /// </summary>
    /// <param name="repositoryRoot">Repository root.</param>
    /// <param name="status">Status entry to compare.</param>
    /// <returns>The loaded comparison or a user-visible error state.</returns>
    internal async Task<GitFileComparison> GetFileComparisonAsync(string repositoryRoot, GitFileStatus status)
    {
        if (status.Bucket == GitStatusBucket.Untracked)
        {
            var workingTreeSide = this.LoadWorkingTreeSide(repositoryRoot, status.Path, "Working tree");
            if (workingTreeSide.Side is null)
            {
                return this.ErrorComparison(status.Path, workingTreeSide.ErrorMessage);
            }

            var rows = this.BuildAddedRows(workingTreeSide.Side.Text);
            return new GitFileComparison(
                status.Path,
                this.CreateAlignedSide(rows, oldSide: true, "Empty"),
                this.CreateAlignedSide(rows, oldSide: false, workingTreeSide.Side.SourceLabel),
                false,
                null);
        }

        var diff = await this.GetDiffAsync(repositoryRoot, status, 0).ConfigureAwait(true);
        if (!diff.Succeeded)
        {
            return this.ErrorComparison(status.Path, diff.ErrorMessage);
        }

        var parsedDiff = GitDiffParser.Parse(diff.Output);
        if (parsedDiff.Any(file => file.IsBinary))
        {
            return new GitFileComparison(status.Path, null, null, true, null);
        }

        var oldSide = await this.LoadOldSideAsync(
            repositoryRoot,
            status,
            Array.Empty<GitDiffHighlightRange>()).ConfigureAwait(true);
        if (oldSide.Side is null)
        {
            return this.ErrorComparison(status.Path, oldSide.ErrorMessage);
        }

        var newSide = await this.LoadNewSideAsync(
            repositoryRoot,
            status,
            Array.Empty<GitDiffHighlightRange>()).ConfigureAwait(true);
        if (newSide.Side is null)
        {
            return this.ErrorComparison(status.Path, newSide.ErrorMessage);
        }

        var changedRows = parsedDiff.FirstOrDefault()?.Rows ?? Array.Empty<GitDiffRow>();
        var alignedRows = GitAlignedDiffBuilder.Build(oldSide.Side.Text, newSide.Side.Text, changedRows);
        return new GitFileComparison(
            status.Path,
            this.CreateAlignedSide(alignedRows, oldSide: true, oldSide.Side.SourceLabel),
            this.CreateAlignedSide(alignedRows, oldSide: false, newSide.Side.SourceLabel),
            false,
            null);
    }

    /// <summary>
    /// Runs Git with the provided arguments.
    /// </summary>
    /// <param name="workingDirectory">Process working directory.</param>
    /// <param name="arguments">Git arguments.</param>
    /// <returns>The Git command result.</returns>
    internal async Task<GitCommandResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments);

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

        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var recordIndex = 0; recordIndex < records.Length; recordIndex++)
        {
            var line = records[recordIndex];
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = line["# branch.head ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = line["# branch.upstream ".Length..].Trim();
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
                var path = this.GetPorcelainPath(line, 9);
                var originalPath = recordIndex + 1 < records.Length
                    ? records[++recordIndex]
                    : null;
                if (indexStatus != '.')
                {
                    staged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Staged, originalPath));
                }

                if (workTreeStatus != '.')
                {
                    unstaged.Add(new GitFileStatus(path, indexStatus, workTreeStatus, GitStatusBucket.Unstaged, originalPath));
                }
            }
            else if (line.StartsWith("u ", StringComparison.Ordinal) && line.Length > 10)
            {
                var path = this.GetPorcelainPath(line, 10);
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
        return this.GetPorcelainPath(line, 8);
    }

    private string GetPorcelainPath(string line, int fieldCount)
    {
        var index = this.IndexOfNthSpace(line, fieldCount);
        if (index < 0 || index + 1 >= line.Length)
        {
            return line;
        }

        return line[(index + 1)..];
    }

    private GitFileComparison ErrorComparison(string path, string? errorMessage)
    {
        return new GitFileComparison(path, null, null, false, errorMessage ?? "Unable to load file comparison.");
    }

    private GitFileSideContent EmptySide(string sourceLabel)
    {
        return new GitFileSideContent(
            string.Empty,
            sourceLabel,
            Array.Empty<GitDiffHighlightRange>(),
            Array.Empty<int?>());
    }

    private IReadOnlyList<GitDiffRow> BuildAddedRows(string text)
    {
        var lineCount = this.CountTextLines(text);
        var rows = new List<GitDiffRow>(lineCount);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lineCount; index++)
        {
            rows.Add(new GitDiffRow(null, null, index + 1, lines[index], GitDiffRowKind.Added));
        }

        return rows;
    }

    private GitFileSideContent CreateAlignedSide(
        IReadOnlyList<GitDiffRow> rows,
        bool oldSide,
        string sourceLabel)
    {
        var text = string.Join(
            "\n",
            rows.Select(row => oldSide ? row.OldText ?? string.Empty : row.NewText ?? string.Empty));
        var lineNumbers = rows
            .Select(row => oldSide ? row.OldLineNumber : row.NewLineNumber)
            .ToArray();
        var highlights = new List<GitDiffHighlightRange>();
        GitDiffHighlightKind? pendingKind = null;
        var pendingStart = 0;

        for (var index = 0; index <= rows.Count; index++)
        {
            var kind = index < rows.Count ? this.ToHighlightKind(rows[index].Kind) : null;
            if (kind == pendingKind)
            {
                continue;
            }

            if (pendingKind is not null)
            {
                highlights.Add(new GitDiffHighlightRange(
                    pendingStart + 1,
                    index - pendingStart,
                    pendingKind.Value));
            }

            pendingKind = kind;
            pendingStart = index;
        }

        return new GitFileSideContent(text, sourceLabel, highlights, lineNumbers);
    }

    private int CountTextLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var count = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private string ToWorkingTreePath(string repositoryRoot, string repositoryRelativePath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private bool ContainsBinaryData(string text)
    {
        foreach (var character in text)
        {
            if (character == '\0')
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(GitFileSideContent? Side, string? ErrorMessage)> LoadOldSideAsync(
        string repositoryRoot,
        GitFileStatus status,
        IReadOnlyList<GitDiffHighlightRange> highlights)
    {
        if (status.Bucket == GitStatusBucket.Staged)
        {
            if (status.IndexStatus == 'A')
            {
                return (this.EmptySide("Empty"), null);
            }

            return await this.LoadGitBlobSideAsync(
                repositoryRoot,
                $"HEAD:{status.OriginalPath ?? status.Path}",
                "HEAD",
                highlights).ConfigureAwait(true);
        }

        return await this.LoadGitBlobSideAsync(
            repositoryRoot,
            $":{status.OriginalPath ?? status.Path}",
            "Index",
            highlights).ConfigureAwait(true);
    }

    private async Task<(GitFileSideContent? Side, string? ErrorMessage)> LoadNewSideAsync(
        string repositoryRoot,
        GitFileStatus status,
        IReadOnlyList<GitDiffHighlightRange> highlights)
    {
        if (status.Bucket == GitStatusBucket.Staged)
        {
            if (status.IndexStatus == 'D')
            {
                return (this.EmptySide("Empty"), null);
            }

            return await this.LoadGitBlobSideAsync(repositoryRoot, $":{status.Path}", "Index", highlights).ConfigureAwait(true);
        }

        if (status.WorkTreeStatus == 'D')
        {
            return (this.EmptySide("Empty"), null);
        }

        return this.LoadWorkingTreeSide(repositoryRoot, status.Path, "Working tree", highlights);
    }

    private async Task<(GitFileSideContent? Side, string? ErrorMessage)> LoadGitBlobSideAsync(
        string repositoryRoot,
        string revisionPath,
        string sourceLabel,
        IReadOnlyList<GitDiffHighlightRange> highlights)
    {
        var result = await this.RunGitAsync(repositoryRoot, "show", revisionPath).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            return (null, result.ErrorMessage);
        }

        if (this.ContainsBinaryData(result.Output))
        {
            return (null, "Binary files cannot be opened in the Git page.");
        }

        if (Encoding.UTF8.GetByteCount(result.Output) > TextEditorService.MaxEditableBytes)
        {
            return (null, $"The file is larger than the {TextEditorService.MaxEditableBytes / 1024 / 1024} MiB editor limit.");
        }

        return (new GitFileSideContent(result.Output, sourceLabel, highlights, Array.Empty<int?>()), null);
    }

    private (GitFileSideContent? Side, string? ErrorMessage) LoadWorkingTreeSide(
        string repositoryRoot,
        string repositoryRelativePath,
        string sourceLabel,
        IReadOnlyList<GitDiffHighlightRange>? highlights = null)
    {
        var result = this.textEditorService.Open(this.ToWorkingTreePath(repositoryRoot, repositoryRelativePath));
        if (result.Document is null)
        {
            return (null, result.ErrorMessage);
        }

        return (new GitFileSideContent(
            result.Document.Text,
            sourceLabel,
            highlights ?? Array.Empty<GitDiffHighlightRange>(),
            Array.Empty<int?>()), null);
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

    private async Task<GitCommandResult> UnstagePathsAsync(
        string repositoryRoot,
        IReadOnlyList<string> paths)
    {
        var headResult = await this.RunGitAsync(repositoryRoot, "rev-parse", "--verify", "HEAD").ConfigureAwait(true);
        var arguments = headResult.Succeeded
            ? new List<string> { "restore", "--staged", "--" }
            : new List<string> { "rm", "--cached", "-r", "--" };
        arguments.AddRange(paths);
        return await this.RunGitAsync(repositoryRoot, arguments.ToArray()).ConfigureAwait(true);
    }

    private GitDiffHighlightKind? ToHighlightKind(GitDiffRowKind kind)
    {
        return kind switch
        {
            GitDiffRowKind.Added => GitDiffHighlightKind.Added,
            GitDiffRowKind.Removed => GitDiffHighlightKind.Removed,
            GitDiffRowKind.Modified => GitDiffHighlightKind.Modified,
            _ => null,
        };
    }
}
