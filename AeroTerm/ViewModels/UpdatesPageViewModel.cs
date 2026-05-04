// <copyright file="UpdatesPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Services;

/// <summary>
/// View model for the Updates settings page. Displays the current version,
/// channel selection, update check status, and available update actions.
/// </summary>
internal sealed class UpdatesPageViewModel : SettingsPageViewModel, INotifyPropertyChanged, ISettingsPageLifecycle
{
    private readonly AppSettings settings;
    private readonly IUpdateService updateService;

    private int selectedChannelIndex;
    private bool autoCheckForUpdates;
    private string statusText = "Not checked yet";
    private bool isUpdateAvailable;
    private bool isChecking;
    private string? releaseNotesUrl;
    private bool isDownloading;
    private int downloadProgress;
    private bool isReadyToRestart;
    private string downloadStatusText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatesPageViewModel"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="updateService">The update service.</param>
    public UpdatesPageViewModel(AppSettings settings, IUpdateService updateService)
    {
        this.settings = settings;
        this.updateService = updateService;
        this.IsInstalled = updateService.IsInstalled;
        this.SupportsAutoUpdate = updateService.SupportsAutoUpdate;

        this.selectedChannelIndex = (int)updateService.InstalledChannel;
        this.autoCheckForUpdates = settings.AutoCheckForUpdates;

        // Stable builds cannot switch to CI — only CI builds can switch to Stable.
        this.CanSwitchToCI = updateService.InstalledChannel != UpdateChannel.Stable;

        this.updateService.UpdateAvailableChanged += this.OnUpdateAvailableChanged;
        this.RefreshStatus();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Updates";

    /// <inheritdoc/>
    public override System.Collections.Generic.IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        SettingsSearchLabels.CurrentVersion,
        SettingsSearchLabels.UpdateChannel,
        SettingsSearchLabels.CheckForUpdatesAutomatically,
        SettingsSearchLabels.LastChecked,
        SettingsSearchLabels.UpdateStatus,
        "Download",
        "Restart to Update",
        "Skip This Version",
        "Release Notes",
        SettingsSearchLabels.CheckForUpdates,
    };

    /// <summary>
    /// Gets the current version text.
    /// </summary>
    public string VersionText { get; } = GetVersionText();

    /// <summary>
    /// Gets a value indicating whether the app is a Velopack-installed build
    /// that can receive updates.
    /// </summary>
    public bool IsInstalled { get; }

    /// <summary>
    /// Gets a value indicating whether auto-update is available.
    /// <c>false</c> for local dev builds.
    /// </summary>
    public bool SupportsAutoUpdate { get; }

    /// <summary>
    /// Gets a value indicating whether the user can switch to the CI channel.
    /// Stable installs cannot switch to CI; CI installs can switch to Stable.
    /// </summary>
    public bool CanSwitchToCI { get; }

    /// <summary>
    /// Gets or sets the selected channel index (0 = Stable, 1 = CI).
    /// </summary>
    public int SelectedChannelIndex
    {
        get => this.selectedChannelIndex;
        set
        {
            if (this.SetField(ref this.selectedChannelIndex, value))
            {
                var target = (UpdateChannel)value;
                if (target != this.updateService.InstalledChannel)
                {
                    _ = this.SwitchChannelAsync(target);
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether auto-check for updates is enabled.
    /// </summary>
    public bool AutoCheckForUpdates
    {
        get => this.autoCheckForUpdates;
        set => this.SetField(ref this.autoCheckForUpdates, value);
    }

    /// <summary>
    /// Gets or sets the update status text.
    /// </summary>
    public string StatusText
    {
        get => this.statusText;
        set => this.SetField(ref this.statusText, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable
    {
        get => this.isUpdateAvailable;
        set => this.SetField(ref this.isUpdateAvailable, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a check is in progress.
    /// </summary>
    public bool IsChecking
    {
        get => this.isChecking;
        set => this.SetField(ref this.isChecking, value);
    }

    /// <summary>
    /// Gets or sets the release notes URL.
    /// </summary>
    public string? ReleaseNotesUrl
    {
        get => this.releaseNotesUrl;
        set => this.SetField(ref this.releaseNotesUrl, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a download is in progress.
    /// </summary>
    public bool IsDownloading
    {
        get => this.isDownloading;
        set => this.SetField(ref this.isDownloading, value);
    }

    /// <summary>
    /// Gets or sets the download progress percentage (0–100).
    /// </summary>
    public int DownloadProgress
    {
        get => this.downloadProgress;
        set => this.SetField(ref this.downloadProgress, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether a downloaded update is ready to restart.
    /// </summary>
    public bool IsReadyToRestart
    {
        get => this.isReadyToRestart;
        set => this.SetField(ref this.isReadyToRestart, value);
    }

    /// <summary>
    /// Gets or sets the download status text.
    /// </summary>
    public string DownloadStatusText
    {
        get => this.downloadStatusText;
        set => this.SetField(ref this.downloadStatusText, value);
    }

    /// <summary>
    /// Gets the last checked text.
    /// </summary>
    public string LastCheckedText => FormatLastChecked(this.settings.LastUpdateCheckUtc);

    /// <summary>
    /// Saves update-related settings when the dialog is accepted.
    /// </summary>
    public void Commit()
    {
        this.settings.AutoCheckForUpdates = this.AutoCheckForUpdates;
    }

    /// <summary>
    /// Performs a manual update check on the installed channel.
    /// </summary>
    /// <returns>A task that completes when the check finishes.</returns>
    public async Task CheckForUpdateAsync()
    {
        this.IsChecking = true;
        this.StatusText = "Checking…";

        await this.updateService.CheckForUpdateAsync().ConfigureAwait(true);

        this.IsChecking = false;
        this.RefreshStatus();
        this.OnPropertyChanged(nameof(this.LastCheckedText));
    }

    /// <summary>
    /// Downloads the update and prepares it for installation.
    /// </summary>
    /// <returns>A task that completes when the download finishes.</returns>
    public async Task DownloadAndInstallAsync()
    {
        this.IsDownloading = true;
        this.DownloadProgress = 0;
        this.DownloadStatusText = "Downloading…";

        var progress = new Progress<int>(p =>
        {
            this.DownloadProgress = p;
            this.DownloadStatusText = $"Downloading… {p}%";
        });

        await this.updateService.DownloadUpdateAsync(progress).ConfigureAwait(true);

        this.IsDownloading = false;

        if (this.updateService.IsReadyToApply)
        {
            this.IsReadyToRestart = true;
            this.DownloadStatusText = "Ready to restart";
        }
        else if (this.updateService.LastError is not null)
        {
            this.DownloadStatusText = $"Download failed: {this.updateService.LastError}";
        }
    }

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// </summary>
    public void RestartToUpdate()
    {
        this.updateService.ApplyUpdateAndRestart();
    }

    /// <summary>
    /// Opens the release notes URL in the default browser.
    /// </summary>
    public void ViewReleaseNotes()
    {
        if (!string.IsNullOrEmpty(this.ReleaseNotesUrl))
        {
            Process.Start(new ProcessStartInfo(this.ReleaseNotesUrl) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// Skips the currently available update version.
    /// </summary>
    public void SkipThisVersion()
    {
        this.updateService.DismissUpdate(skipVersion: true);
    }

    private static string GetVersionText()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var versionText = informationalVersion ?? "Unknown";
        if (versionText.Split('+') is [var version, var build])
        {
            versionText = $"{version.Trim()} build {build[..Math.Min(7, build.Length)]}";
        }

        return versionText;
    }

    private static string FormatLastChecked(DateTime? utc)
    {
        if (utc is null)
        {
            return "Never";
        }

        var elapsed = DateTime.UtcNow - utc.Value;
        if (elapsed.TotalMinutes < 1)
        {
            return "Just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} minute(s) ago";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{(int)elapsed.TotalHours} hour(s) ago";
        }

        return utc.Value.ToLocalTime().ToString("g");
    }

    private async Task SwitchChannelAsync(UpdateChannel target)
    {
        this.IsChecking = true;
        this.StatusText = $"Checking {target} channel…";

        await this.updateService.CheckForUpdateAsync(target).ConfigureAwait(true);

        this.IsChecking = false;
        this.RefreshStatus();
        this.OnPropertyChanged(nameof(this.LastCheckedText));
    }

    private void OnUpdateAvailableChanged(object? sender, UpdateInfo? info)
    {
        this.RefreshStatus();
    }

    private void RefreshStatus()
    {
        var update = this.updateService.AvailableUpdate;
        if (update is not null)
        {
            this.IsUpdateAvailable = true;
            this.StatusText = $"Update available: {update.Version}";
            this.ReleaseNotesUrl = update.ReleaseNotesUrl;
        }
        else if (this.updateService.LastError is not null)
        {
            this.IsUpdateAvailable = false;
            this.StatusText = $"Check failed: {this.updateService.LastError}";
            this.ReleaseNotesUrl = null;
        }
        else
        {
            this.IsUpdateAvailable = false;
            this.StatusText = "You're up to date";
            this.ReleaseNotesUrl = null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}
