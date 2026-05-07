// <copyright file="UpdateService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

/// <summary>
/// Checks for, downloads, and applies application updates using Velopack.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    /// <summary>
    /// Base URL for the GitHub Pages update feed (used by CI channel).
    /// </summary>
    internal const string GitHubPagesFeedUrl = "https://dontpanic92.github.io/aeroterm/updates";

    /// <summary>
    /// GitHub repository URL used by Velopack's <see cref="GithubSource"/>
    /// to fetch stable releases from GitHub Releases.
    /// </summary>
    internal const string GitHubRepoUrl = "https://github.com/dontpanic92/aeroterm";

    private static readonly ILogger Log = AppLogger.For<UpdateService>();

    private readonly AppSettings settings;
    private readonly Func<UpdateChannel, UpdateManager>? managerFactory;

    private UpdateManager? currentManager;
    private UpdateChannel? currentManagerChannel;
    private Velopack.UpdateInfo? velopackUpdateInfo;
    private UpdateInfo? availableUpdate;
    private UpdateChannel? lastCheckedChannel;
    private bool isChecking;
    private bool isDownloading;
    private int downloadProgress;
    private bool isReadyToApply;
    private string? lastError;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="managerFactory">Optional factory for testing.</param>
    public UpdateService(AppSettings settings, Func<UpdateChannel, UpdateManager>? managerFactory = null)
    {
        this.settings = settings;
        this.managerFactory = managerFactory;
    }

    /// <inheritdoc/>
    public event EventHandler<UpdateInfo?>? UpdateAvailableChanged;

    /// <inheritdoc/>
    public UpdateInfo? AvailableUpdate => this.availableUpdate;

    /// <inheritdoc/>
    public bool IsInstalled
    {
        get
        {
            try
            {
                var mgr = this.GetOrCreateManager(this.InstalledChannel);
                return mgr.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public UpdateChannel InstalledChannel => DetectInstalledChannel();

    /// <inheritdoc/>
    public bool SupportsAutoUpdate => this.IsInstalled && IsRecognizedUpdateChannel();

    /// <inheritdoc/>
    public bool IsChecking => this.isChecking;

    /// <inheritdoc/>
    public bool IsDownloading => this.isDownloading;

    /// <inheritdoc/>
    public int DownloadProgress => this.downloadProgress;

    /// <inheritdoc/>
    public bool IsReadyToApply => this.isReadyToApply;

    /// <inheritdoc/>
    public string? LastError => this.lastError;

    /// <inheritdoc/>
    public async Task CheckForUpdateAsync(UpdateChannel? targetChannel = null, CancellationToken ct = default)
    {
        if (this.isChecking)
        {
            return;
        }

        this.isChecking = true;
        this.lastError = null;

        try
        {
            var channel = targetChannel ?? this.InstalledChannel;

            // When switching to a different channel, clear stale state so the
            // previous channel's skipped version and pending download don't
            // interfere with the new channel's update check.
            if (this.lastCheckedChannel is not null && this.lastCheckedChannel != channel)
            {
                Log.LogInformation("Target channel changed to {Channel} — clearing stale state.", channel);
                this.settings.SkippedVersion = null;
                this.velopackUpdateInfo = null;
                this.isReadyToApply = false;
                this.SetAvailableUpdate(null);
            }

            this.lastCheckedChannel = channel;

            var mgr = this.GetOrCreateManager(channel);

            if (!mgr.IsInstalled)
            {
                Log.LogInformation("Skipping update check — not a Velopack-installed build (local dev build).");
                return;
            }

            if (targetChannel is null && !IsRecognizedUpdateChannel())
            {
                Log.LogInformation("Skipping update check — installed channel is not a recognized update channel.");
                return;
            }

            Log.LogInformation("Checking for updates on {Channel} channel via Velopack.", channel);

            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);

            this.settings.LastUpdateCheckUtc = DateTime.UtcNow;

            if (info is null)
            {
                Log.LogInformation("Already up to date.");
                this.velopackUpdateInfo = null;
                this.SetAvailableUpdate(null);
                return;
            }

            var version = info.TargetFullRelease.Version.ToString();

            // If the user previously skipped this exact version, don't notify again.
            if (!string.IsNullOrEmpty(this.settings.SkippedVersion) &&
                string.Equals(this.settings.SkippedVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogInformation("Skipping previously dismissed version {Version}.", version);
                return;
            }

            this.velopackUpdateInfo = info;

            var update = new UpdateInfo(
                version,
                channel,
                string.Empty,
                GetReleaseNotesUrl(channel),
                null);

            Log.LogInformation("Update available: {Version}.", version);
            this.SetAvailableUpdate(update);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled.
        }
        catch (Exception ex)
        {
            this.lastError = ex.Message;
            Log.LogWarning(ex, "Update check failed: {Message}", ex.Message);
        }
        finally
        {
            this.isChecking = false;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (this.isDownloading || this.velopackUpdateInfo is null || this.lastCheckedChannel is null)
        {
            return;
        }

        this.isDownloading = true;
        this.downloadProgress = 0;
        this.lastError = null;

        try
        {
            var mgr = this.GetOrCreateManager(this.lastCheckedChannel.Value);

            if (!mgr.IsInstalled)
            {
                Log.LogWarning("Cannot download update — not a Velopack-installed build.");
                this.lastError = "Updates are not available in local dev builds.";
                return;
            }

            await mgr.DownloadUpdatesAsync(
                this.velopackUpdateInfo,
                p =>
                {
                    this.downloadProgress = p;
                    progress?.Report(p);
                }).ConfigureAwait(false);

            this.isReadyToApply = true;
            Log.LogInformation("Update downloaded and ready to apply.");
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled.
        }
        catch (Exception ex)
        {
            this.lastError = ex.Message;
            Log.LogWarning(ex, "Update download failed: {Message}", ex.Message);
        }
        finally
        {
            this.isDownloading = false;
        }
    }

    /// <inheritdoc/>
    public void ApplyUpdateAndRestart()
    {
        if (this.velopackUpdateInfo is null || this.lastCheckedChannel is null)
        {
            return;
        }

        try
        {
            var mgr = this.GetOrCreateManager(this.lastCheckedChannel.Value);

            if (!mgr.IsInstalled)
            {
                Log.LogWarning("Cannot apply update — not a Velopack-installed build.");
                return;
            }

            Log.LogInformation("Applying update and restarting…");
            mgr.ApplyUpdatesAndRestart(this.velopackUpdateInfo);
        }
        catch (Exception ex)
        {
            this.lastError = ex.Message;
            Log.LogError(ex, "Failed to apply update: {Message}", ex.Message);
        }
    }

    /// <inheritdoc/>
    public void DismissUpdate(bool skipVersion)
    {
        if (skipVersion && this.availableUpdate is not null)
        {
            this.settings.SkippedVersion = this.availableUpdate.Version;
        }

        this.velopackUpdateInfo = null;
        this.isReadyToApply = false;
        this.SetAvailableUpdate(null);
    }

    /// <summary>
    /// Returns <c>true</c> when the Velopack channel string ends with
    /// <c>-stable</c> or <c>-ci</c> — the only channels with a
    /// corresponding update feed.
    /// </summary>
    private static bool IsRecognizedUpdateChannel()
    {
        try
        {
            var channel = VelopackLocator.Current?.Channel;
            if (!string.IsNullOrEmpty(channel))
            {
                return channel.EndsWith("-stable", StringComparison.OrdinalIgnoreCase)
                    || channel.EndsWith("-ci", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Not a Velopack install or locator unavailable.
        }

        return false;
    }

    /// <summary>
    /// Detects the installed channel from the Velopack manifest. The channel
    /// name follows the convention <c>{rid}-{stable|ci}</c>.
    /// </summary>
    private static UpdateChannel DetectInstalledChannel()
    {
        try
        {
            var channel = VelopackLocator.Current?.Channel;
            if (!string.IsNullOrEmpty(channel))
            {
                if (channel.EndsWith("-ci", StringComparison.OrdinalIgnoreCase))
                {
                    return UpdateChannel.CI;
                }
            }
        }
        catch
        {
            // Not a Velopack install or locator unavailable.
        }

        return UpdateChannel.Stable;
    }

    private static string GetReleaseNotesUrl(UpdateChannel channel) => channel switch
    {
        UpdateChannel.CI => "https://github.com/dontpanic92/aeroterm/commits/master",
        _ => "https://github.com/dontpanic92/aeroterm/releases/latest",
    };

    private static string GetChannelName(UpdateChannel channel)
    {
        var rid = GetCurrentRuntimeIdentifier() ?? "unknown";
        var suffix = channel switch
        {
            UpdateChannel.CI => "ci",
            _ => "stable",
        };
        return $"{rid}-{suffix}";
    }

    private static string? GetCurrentRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux-x64";
        }

        return null;
    }

    private UpdateManager GetOrCreateManager(UpdateChannel channel)
    {
        if (this.managerFactory is not null)
        {
            return this.managerFactory(channel);
        }

        if (this.currentManager is not null && this.currentManagerChannel == channel)
        {
            return this.currentManager;
        }

        var channelName = GetChannelName(channel);
        var options = new UpdateOptions
        {
            ExplicitChannel = channelName,
            AllowVersionDowngrade = true,
        };

        // Use a custom downloader that disables HTTP response decompression so
        // the response Content-Length header is preserved — without it,
        // Velopack's downloader cannot report intermediate progress and the
        // update progress bar jumps straight from 0 to 100.
        var downloader = new AeroTermVelopackFileDownloader();

        if (channel == UpdateChannel.Stable)
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false, downloader);
            this.currentManager = new UpdateManager(source, options);
        }
        else
        {
            var source = new SimpleWebSource(GitHubPagesFeedUrl, downloader);
            this.currentManager = new UpdateManager(source, options);
        }

        this.currentManagerChannel = channel;
        return this.currentManager;
    }

    private void SetAvailableUpdate(UpdateInfo? update)
    {
        this.availableUpdate = update;
        this.UpdateAvailableChanged?.Invoke(this, update);
    }
}
