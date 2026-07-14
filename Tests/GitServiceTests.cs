// <copyright file="GitServiceTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for the Workbench Git CLI service.
/// </summary>
[TestFixture]
public sealed class GitServiceTests
{
    private string tempDir = string.Empty;
    private string remoteDir = string.Empty;

    /// <summary>
    /// Creates a temporary directory for each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            Path.GetTempPath(),
            "git-test-" + Guid.NewGuid().ToString("N"));
        this.remoteDir = this.tempDir + "-remote.git";
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            ClearReadOnlyAttributes(this.tempDir);
            Directory.Delete(this.tempDir, recursive: true);
        }

        if (Directory.Exists(this.remoteDir))
        {
            ClearReadOnlyAttributes(this.remoteDir);
            Directory.Delete(this.remoteDir, recursive: true);
        }
    }

    /// <summary>
    /// Non-repository directories produce the Git empty state.
    /// </summary>
    /// <returns>A task that completes when status detection has finished.</returns>
    [Test]
    public async Task GetStatusAsync_OutsideRepositoryReturnsEmptyState()
    {
        var service = new GitService();
        await this.IgnoreIfGitIsUnavailableAsync(service).ConfigureAwait(false);

        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);

        Assert.That(status.IsRepository, Is.False);
        Assert.That(status.ErrorMessage, Does.Contain("not inside"));
    }

    /// <summary>
    /// Porcelain v2 status is parsed into staged, unstaged, and untracked buckets.
    /// </summary>
    /// <returns>A task that completes when repository setup and status parsing have finished.</returns>
    [Test]
    public async Task GetStatusAsync_ParsesPorcelainStatusBuckets()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);

        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(this.tempDir, "staged.txt"), "staged", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(this.tempDir, "untracked.txt"), "new", new UTF8Encoding(false));
        var addResult = await service.RunGitAsync(this.tempDir, "add", "staged.txt").ConfigureAwait(false);
        Assert.That(addResult.Succeeded, Is.True, addResult.ErrorMessage);

        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);

        Assert.That(status.IsRepository, Is.True);
        Assert.That(status.RepositoryRoot, Does.EndWith(Path.GetFileName(this.tempDir)));
        Assert.That(status.Staged.Select(entry => entry.Path), Contains.Item("staged.txt"));
        Assert.That(status.Unstaged.Select(entry => entry.Path), Contains.Item("tracked.txt"));
        Assert.That(status.Untracked.Select(entry => entry.Path), Contains.Item("untracked.txt"));
    }

    /// <summary>
    /// Null-delimited porcelain status preserves spaces, Unicode, and rename source paths.
    /// </summary>
    /// <returns>A task that completes when status parsing has finished.</returns>
    [Test]
    public async Task GetStatusAsync_PreservesSpecialPathsAndRenames()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var oldPath = Path.Combine(this.tempDir, "tracked.txt");
        var newPath = Path.Combine(this.tempDir, "renamed name.txt");
        File.Move(oldPath, newPath);
        var renameResult = await service.RunGitAsync(
            this.tempDir,
            "add",
            "-A",
            "--",
            "tracked.txt",
            "renamed name.txt").ConfigureAwait(false);
        Assert.That(renameResult.Succeeded, Is.True, renameResult.ErrorMessage);
        File.WriteAllText(Path.Combine(this.tempDir, "new 名.txt"), "new", new UTF8Encoding(false));

        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);

        var renamed = status.Staged.Single(entry => entry.Path == "renamed name.txt");
        Assert.That(renamed.OriginalPath, Is.EqualTo("tracked.txt"));
        Assert.That(status.Untracked.Select(entry => entry.Path), Contains.Item("new 名.txt"));
    }

    /// <summary>
    /// Unstaged modifications compare index content against working-tree content.
    /// </summary>
    /// <returns>A task that completes when comparison loading has finished.</returns>
    [Test]
    public async Task GetFileComparisonAsync_UnstagedModificationLoadsOldAndNewSides()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        var entry = status.Unstaged.Single(item => item.Path == "tracked.txt");

        var comparison = await service.GetFileComparisonAsync(status.RepositoryRoot!, entry).ConfigureAwait(false);

        Assert.That(comparison.Succeeded, Is.True, comparison.ErrorMessage);
        Assert.That(comparison.OldSide!.Text, Is.EqualTo("initial"));
        Assert.That(comparison.OldSide.SourceLabel, Is.EqualTo("Index"));
        Assert.That(comparison.NewSide!.Text, Is.EqualTo("changed"));
        Assert.That(comparison.NewSide.SourceLabel, Is.EqualTo("Working tree"));
        Assert.That(comparison.OldSide.Highlights[0].Kind, Is.EqualTo(GitDiffHighlightKind.Modified));
        Assert.That(comparison.NewSide.Highlights[0].Kind, Is.EqualTo(GitDiffHighlightKind.Modified));
    }

    /// <summary>
    /// Staged additions compare an empty old side against the index blob.
    /// </summary>
    /// <returns>A task that completes when comparison loading has finished.</returns>
    [Test]
    public async Task GetFileComparisonAsync_StagedAdditionLoadsIndexAsNewSide()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "added.txt"), "added", new UTF8Encoding(false));
        var addResult = await service.RunGitAsync(this.tempDir, "add", "added.txt").ConfigureAwait(false);
        Assert.That(addResult.Succeeded, Is.True, addResult.ErrorMessage);
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        var entry = status.Staged.Single(item => item.Path == "added.txt");

        var comparison = await service.GetFileComparisonAsync(status.RepositoryRoot!, entry).ConfigureAwait(false);

        Assert.That(comparison.Succeeded, Is.True, comparison.ErrorMessage);
        Assert.That(comparison.OldSide!.Text, Is.Empty);
        Assert.That(comparison.NewSide!.Text, Is.EqualTo("added"));
        Assert.That(comparison.NewSide.Highlights, Is.EqualTo(new[] { new GitDiffHighlightRange(1, 1, GitDiffHighlightKind.Added) }));
    }

    /// <summary>
    /// Untracked files compare an empty old side against the working-tree file.
    /// </summary>
    /// <returns>A task that completes when comparison loading has finished.</returns>
    [Test]
    public async Task GetFileComparisonAsync_UntrackedFileHighlightsWholeNewSide()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "untracked.txt"), "first\nsecond", new UTF8Encoding(false));
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        var entry = status.Untracked.Single(item => item.Path == "untracked.txt");

        var comparison = await service.GetFileComparisonAsync(status.RepositoryRoot!, entry).ConfigureAwait(false);

        Assert.That(comparison.Succeeded, Is.True, comparison.ErrorMessage);
        Assert.That(comparison.OldSide!.Text, Is.EqualTo("\n"));
        Assert.That(comparison.OldSide.LineNumbers, Is.EqualTo(new int?[] { null, null }));
        Assert.That(comparison.NewSide!.Text, Is.EqualTo("first\nsecond"));
        Assert.That(comparison.NewSide.LineNumbers, Is.EqualTo(new int?[] { 1, 2 }));
        Assert.That(comparison.NewSide.Highlights, Is.EqualTo(new[] { new GitDiffHighlightRange(1, 2, GitDiffHighlightKind.Added) }));
    }

    /// <summary>
    /// File-level and bulk operations update staged, unstaged, and untracked state.
    /// </summary>
    /// <returns>A task that completes when all Git operations finish.</returns>
    [Test]
    public async Task GitActions_StageUnstageAndDeleteUntrackedPaths()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(this.tempDir, "untracked.txt"), "new", new UTF8Encoding(false));

        var stageAll = await service.StageAllAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(stageAll.Succeeded, Is.True, stageAll.ErrorMessage);
        var stagedStatus = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(stagedStatus.Staged.Select(entry => entry.Path), Does.Contain("tracked.txt"));
        Assert.That(stagedStatus.Staged.Select(entry => entry.Path), Does.Contain("untracked.txt"));

        var unstageAll = await service.UnstageAllAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(unstageAll.Succeeded, Is.True, unstageAll.ErrorMessage);
        var unstagedStatus = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(unstagedStatus.Unstaged.Select(entry => entry.Path), Does.Contain("tracked.txt"));
        Assert.That(unstagedStatus.Untracked.Select(entry => entry.Path), Does.Contain("untracked.txt"));

        var deleteUntracked = await service.DeleteUntrackedAsync(this.tempDir, "untracked.txt").ConfigureAwait(false);
        Assert.That(deleteUntracked.Succeeded, Is.True, deleteUntracked.ErrorMessage);
        Assert.That(File.Exists(Path.Combine(this.tempDir, "untracked.txt")), Is.False);
    }

    /// <summary>
    /// Sync pulls the configured upstream and pushes local commits to it.
    /// </summary>
    /// <returns>A task that completes when the local and bare repositories synchronize.</returns>
    [Test]
    public async Task SyncAsync_WithUpstreamPushesLocalCommit()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var bareInit = await service.RunGitAsync(this.tempDir, "init", "--bare", this.remoteDir).ConfigureAwait(false);
        Assert.That(bareInit.Succeeded, Is.True, bareInit.ErrorMessage);
        var addRemote = await service.RunGitAsync(
            this.tempDir,
            "remote",
            "add",
            "origin",
            this.remoteDir).ConfigureAwait(false);
        Assert.That(addRemote.Succeeded, Is.True, addRemote.ErrorMessage);
        var initialPush = await service.RunGitAsync(
            this.tempDir,
            "push",
            "-u",
            "origin",
            "HEAD").ConfigureAwait(false);
        Assert.That(initialPush.Succeeded, Is.True, initialPush.ErrorMessage);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "synced", new UTF8Encoding(false));
        var commit = await service.RunGitAsync(
            this.tempDir,
            "commit",
            "-am",
            "sync change").ConfigureAwait(false);
        Assert.That(commit.Succeeded, Is.True, commit.ErrorMessage);

        var sync = await service.SyncAsync(this.tempDir).ConfigureAwait(false);

        Assert.That(sync.Succeeded, Is.True, sync.ErrorMessage);
        var remoteContent = await service.RunGitAsync(
            this.tempDir,
            $"--git-dir={this.remoteDir}",
            "show",
            "HEAD:tracked.txt").ConfigureAwait(false);
        Assert.That(remoteContent.Succeeded, Is.True, remoteContent.ErrorMessage);
        Assert.That(remoteContent.Output, Is.EqualTo("synced"));
    }

    /// <summary>
    /// Unstaging a rename clears both destination and original index entries.
    /// </summary>
    /// <returns>A task that completes when the rename is unstaged.</returns>
    [Test]
    public async Task UnstageAsync_StagedRenameUnstagesBothPaths()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.Move(
            Path.Combine(this.tempDir, "tracked.txt"),
            Path.Combine(this.tempDir, "renamed.txt"));
        var stage = await service.StageAllAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(stage.Succeeded, Is.True, stage.ErrorMessage);
        var stagedStatus = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        var rename = stagedStatus.Staged.Single(entry => entry.Path == "renamed.txt");

        var unstage = await service.UnstageAsync(this.tempDir, rename).ConfigureAwait(false);

        Assert.That(unstage.Succeeded, Is.True, unstage.ErrorMessage);
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(status.Staged, Is.Empty);
        Assert.That(status.Unstaged.Select(entry => entry.Path), Does.Contain("tracked.txt"));
        Assert.That(status.Untracked.Select(entry => entry.Path), Does.Contain("renamed.txt"));
    }

    /// <summary>
    /// Unstage operations work in a repository before its first commit.
    /// </summary>
    /// <returns>A task that completes when the unborn index is cleared.</returns>
    [Test]
    public async Task UnstageAllAsync_UnbornRepositoryClearsIndex()
    {
        var service = new GitService();
        await this.IgnoreIfGitIsUnavailableAsync(service).ConfigureAwait(false);
        var init = await service.RunGitAsync(this.tempDir, "init").ConfigureAwait(false);
        Assert.That(init.Succeeded, Is.True, init.ErrorMessage);
        File.WriteAllText(Path.Combine(this.tempDir, "new.txt"), "new", new UTF8Encoding(false));
        var stage = await service.StageAllAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(stage.Succeeded, Is.True, stage.ErrorMessage);

        var unstage = await service.UnstageAllAsync(this.tempDir).ConfigureAwait(false);

        Assert.That(unstage.Succeeded, Is.True, unstage.ErrorMessage);
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        Assert.That(status.Staged, Is.Empty);
        Assert.That(status.Untracked.Select(entry => entry.Path), Does.Contain("new.txt"));
    }

    /// <summary>
    /// Git commands are configured for hidden, non-shell execution with captured output.
    /// </summary>
    [Test]
    public void CreateStartInfo_ConfiguresHiddenRedirectedProcess()
    {
        var startInfo = GitService.CreateStartInfo(this.tempDir, "status", "--short");

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo("git"));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(this.tempDir));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.CreateNoWindow, Is.True);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
            Assert.That(startInfo.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.StandardErrorEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "-C", this.tempDir, "status", "--short" }));
        });
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        var directoryAttributes = File.GetAttributes(directory);
        if ((directoryAttributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(directory, directoryAttributes & ~FileAttributes.ReadOnly);
        }
    }

    private async Task InitializeRepositoryAsync(GitService service)
    {
        await this.IgnoreIfGitIsUnavailableAsync(service).ConfigureAwait(false);
        var initResult = await service.RunGitAsync(this.tempDir, "init").ConfigureAwait(false);
        Assert.That(initResult.Succeeded, Is.True, initResult.ErrorMessage);
        var nameResult = await service.RunGitAsync(this.tempDir, "config", "user.name", "AeroTerm Tests").ConfigureAwait(false);
        Assert.That(nameResult.Succeeded, Is.True, nameResult.ErrorMessage);
        var emailResult = await service.RunGitAsync(this.tempDir, "config", "user.email", "tests@example.invalid").ConfigureAwait(false);
        Assert.That(emailResult.Succeeded, Is.True, emailResult.ErrorMessage);

        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "initial", new UTF8Encoding(false));
        var addResult = await service.RunGitAsync(this.tempDir, "add", "tracked.txt").ConfigureAwait(false);
        Assert.That(addResult.Succeeded, Is.True, addResult.ErrorMessage);
        var commitResult = await service.RunGitAsync(this.tempDir, "commit", "-m", "initial").ConfigureAwait(false);
        Assert.That(commitResult.Succeeded, Is.True, commitResult.ErrorMessage);
    }

    private async Task IgnoreIfGitIsUnavailableAsync(GitService service)
    {
        var version = await service.RunGitAsync(this.tempDir, "--version").ConfigureAwait(false);
        if (!version.Succeeded)
        {
            Assert.Ignore("git is not available on PATH.");
        }
    }
}
