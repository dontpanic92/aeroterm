// <copyright file="ProfilesPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AeroTerm.Dialogs;
using AeroTerm.Services;
using Avalonia.Platform.Storage;

/// <summary>
/// View model for the Profiles settings page. Backs a list of user
/// <see cref="Profile"/> entries plus an inline editor for the
/// currently-selected profile. The MVP profile model only edits launch
/// fields (name / executable / arguments / working directory). Changes
/// persist through <see cref="ProfileStore.Save"/>;
/// <see cref="App.ReloadProfiles"/> is invoked to broadcast the change so
/// any open new-tab dropdown is rebuilt.
/// </summary>
internal sealed class ProfilesPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly ProfileStore store;
    private Profile? selectedProfile;
    private string? defaultProfileId;
    private Func<IStorageProvider?>? storageProviderFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesPageViewModel"/> class.
    /// </summary>
    /// <param name="store">The profile store to persist through.</param>
    public ProfilesPageViewModel(ProfileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.Profiles = new ObservableCollection<Profile>();
        this.Rebuild();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Profiles";

    /// <summary>
    /// Gets the editable profile list.
    /// </summary>
    public ObservableCollection<Profile> Profiles { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels => new[]
    {
        SettingsSearchLabels.ProfileName,
        SettingsSearchLabels.Command,
        SettingsSearchLabels.Arguments,
        SettingsSearchLabels.WorkingDirectory,
        SettingsSearchLabels.DefaultProfile,
    };

    /// <summary>
    /// Gets or sets the currently-edited profile.
    /// </summary>
    public Profile? SelectedProfile
    {
        get => this.selectedProfile;
        set
        {
            if (!ReferenceEquals(this.selectedProfile, value))
            {
                this.selectedProfile = value;
                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(this.HasSelection));
                this.OnPropertyChanged(nameof(this.SelectedName));
                this.OnPropertyChanged(nameof(this.SelectedCommand));
                this.OnPropertyChanged(nameof(this.SelectedArgsText));
                this.OnPropertyChanged(nameof(this.SelectedWorkingDirectory));
                this.OnPropertyChanged(nameof(this.IsSelectedDefault));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a profile is selected.
    /// </summary>
    public bool HasSelection => this.selectedProfile is not null;

    /// <summary>
    /// Gets or sets the selected profile's display name.
    /// </summary>
    public string SelectedName
    {
        get => this.selectedProfile?.Name ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.Name = value ?? string.Empty;
            this.OnPropertyChanged();
            this.RefreshProfileListEntry(this.selectedProfile);
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's command.
    /// </summary>
    public string SelectedCommand
    {
        get => this.selectedProfile?.Command ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.Command = string.IsNullOrWhiteSpace(value) ? null : value;
            this.OnPropertyChanged();
            this.RefreshProfileListEntry(this.selectedProfile);
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's arguments as a whitespace-joined string.
    /// </summary>
    public string SelectedArgsText
    {
        get => this.selectedProfile?.Args is { } a ? string.Join(' ', a) : string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.Args = string.IsNullOrWhiteSpace(value)
                ? null
                : ParseArgs(value);
            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's working directory.
    /// </summary>
    public string SelectedWorkingDirectory
    {
        get => this.selectedProfile?.WorkingDirectory ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.WorkingDirectory = string.IsNullOrWhiteSpace(value) ? null : value;
            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the selected profile is the default profile.
    /// </summary>
    public bool IsSelectedDefault
        => this.selectedProfile is not null && this.selectedProfile.Id == this.defaultProfileId;

    /// <summary>
    /// Wires the page-supplied storage provider (for Browse… file/folder
    /// pickers). Called by the page's code-behind once the visual is
    /// attached to a top-level.
    /// </summary>
    /// <param name="factory">Delegate returning the active top-level's
    /// <see cref="IStorageProvider"/>, or <c>null</c> when unavailable.</param>
    public void AttachStorageProvider(Func<IStorageProvider?> factory)
    {
        this.storageProviderFactory = factory;
    }

    /// <summary>
    /// Adds a new profile and selects it.
    /// </summary>
    public void AddProfile()
    {
        var profile = new Profile { Name = "New profile" };
        this.Profiles.Add(profile);
        this.SelectedProfile = profile;
        this.Persist();
    }

    /// <summary>
    /// Duplicates the selected profile and selects the clone.
    /// </summary>
    public void DuplicateSelected()
    {
        if (this.selectedProfile is null)
        {
            return;
        }

        var source = this.selectedProfile;
        var clone = new Profile
        {
            Name = source.Name + " (copy)",
            Command = source.Command,
            Args = source.Args is null ? null : (string[])source.Args.Clone(),
            WorkingDirectory = source.WorkingDirectory,
        };
        this.Profiles.Add(clone);
        this.SelectedProfile = clone;
        this.Persist();
    }

    /// <summary>
    /// Removes the selected profile.
    /// </summary>
    public void RemoveSelected()
    {
        if (this.selectedProfile is null)
        {
            return;
        }

        var target = this.selectedProfile;
        int idx = this.Profiles.IndexOf(target);
        this.Profiles.Remove(target);
        if (this.defaultProfileId == target.Id)
        {
            this.defaultProfileId = this.Profiles.Count > 0 ? this.Profiles[0].Id : null;
            this.RefreshDefaultMarkers();
        }

        this.SelectedProfile = this.Profiles.Count == 0
            ? null
            : this.Profiles[Math.Min(idx, this.Profiles.Count - 1)];
        this.Persist();
    }

    /// <summary>
    /// Marks the selected profile as the default profile used for new
    /// tabs opened via the "+" button's primary action.
    /// </summary>
    public void SetSelectedAsDefault()
    {
        if (this.selectedProfile is null)
        {
            return;
        }

        this.defaultProfileId = this.selectedProfile.Id;
        this.RefreshDefaultMarkers();
        this.OnPropertyChanged(nameof(this.IsSelectedDefault));
        this.Persist();
    }

    /// <summary>
    /// Opens a file picker to choose the selected profile's executable
    /// path. No-op when no profile is selected or the storage provider
    /// has not been attached yet.
    /// </summary>
    /// <returns>A task that completes when the picker closes.</returns>
    public async Task BrowseExecutableAsync()
    {
        if (this.selectedProfile is null)
        {
            return;
        }

        var provider = this.storageProviderFactory?.Invoke();
        if (provider is null || !provider.CanOpen)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select shell executable",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            this.SelectedCommand = path;
        }
    }

    /// <summary>
    /// Opens a folder picker to choose the selected profile's working
    /// directory. No-op when no profile is selected or the storage
    /// provider has not been attached yet.
    /// </summary>
    /// <returns>A task that completes when the picker closes.</returns>
    public async Task BrowseWorkingDirectoryAsync()
    {
        if (this.selectedProfile is null)
        {
            return;
        }

        var provider = this.storageProviderFactory?.Invoke();
        if (provider is null || !provider.CanPickFolder)
        {
            return;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select working directory",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            this.SelectedWorkingDirectory = path;
        }
    }

    private static string[] ParseArgs(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        foreach (var c in input)
        {
            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' || c == '\'')
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.ToArray();
    }

    private void Rebuild()
    {
        this.Profiles.Clear();
        var data = this.store.Load();
        foreach (var p in data.Profiles)
        {
            this.Profiles.Add(p);
        }

        this.defaultProfileId = data.DefaultProfileId;
        this.RefreshDefaultMarkers();
        this.SelectedProfile = this.Profiles.FirstOrDefault();
    }

    /// <summary>
    /// Synchronizes <see cref="Profile.IsDefault"/> across the profile
    /// list with <see cref="defaultProfileId"/>, then re-emits each entry
    /// through the bound <see cref="ObservableCollection{T}"/> so dependent
    /// controls (e.g. the NativeDropdown that doesn't observe item-level
    /// INotifyPropertyChanged) pick up the new label.
    /// </summary>
    private void RefreshDefaultMarkers()
    {
        for (int i = 0; i < this.Profiles.Count; i++)
        {
            var p = this.Profiles[i];
            bool shouldBeDefault = p.Id == this.defaultProfileId;
            if (p.IsDefault != shouldBeDefault)
            {
                p.IsDefault = shouldBeDefault;
                this.Profiles[i] = p;
            }
        }
    }

    private void Persist()
    {
        this.store.Save(this.Profiles, this.defaultProfileId);
        App.ReloadProfiles();
    }

    /// <summary>
    /// Forces the bound list view to redraw the row for
    /// <paramref name="profile"/> after an inline edit. ObservableCollection
    /// only raises change notifications on add/remove/replace, so a
    /// title-tweak in the editor would otherwise leave the sidebar stale.
    /// </summary>
    private void RefreshProfileListEntry(Profile profile)
    {
        int idx = this.Profiles.IndexOf(profile);
        if (idx < 0)
        {
            return;
        }

        this.Profiles[idx] = profile;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
