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
using AeroTerm.Dialogs;
using AeroTerm.Models;
using AeroTerm.Services;
using AeroTerm.WindowEffects;

/// <summary>
/// View model for the Profiles settings page. Backs a list of user
/// <see cref="Profile"/> entries plus an inline editor for the
/// currently-selected profile. Changes persist through
/// <see cref="ProfileStore.Save"/>; <see cref="App.ReloadProfiles"/> is
/// invoked to broadcast the change so any open new-tab dropdown is
/// rebuilt.
/// </summary>
internal sealed class ProfilesPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly ProfileStore store;
    private Profile? selectedProfile;
    private string? defaultProfileId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesPageViewModel"/> class.
    /// </summary>
    /// <param name="store">The profile store to persist through.</param>
    public ProfilesPageViewModel(ProfileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.Profiles = new ObservableCollection<Profile>();
        this.ColorSchemes = new List<string>(new[] { string.Empty }.Concat(ColorSchemePresets.All.Select(s => s.Name)));
        this.WindowEffects = new List<string>(new[] { string.Empty }.Concat(Enum.GetNames<BlurType>()));
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

    /// <summary>
    /// Gets the list of color scheme names offered in the editor. An
    /// empty string means "use application default".
    /// </summary>
    public IReadOnlyList<string> ColorSchemes { get; }

    /// <summary>
    /// Gets the list of window effect names offered in the editor. An
    /// empty string means "use application default".
    /// </summary>
    public IReadOnlyList<string> WindowEffects { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels => new[]
    {
        SettingsSearchLabels.ProfileName,
        SettingsSearchLabels.Command,
        SettingsSearchLabels.Arguments,
        SettingsSearchLabels.WorkingDirectory,
        SettingsSearchLabels.EnvironmentOverrides,
        SettingsSearchLabels.ColorScheme,
        SettingsSearchLabels.FontFamilies,
        SettingsSearchLabels.FontSize,
        SettingsSearchLabels.WindowEffect,
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
                this.OnPropertyChanged(nameof(this.SelectedEnvironmentText));
                this.OnPropertyChanged(nameof(this.SelectedColorSchemeName));
                this.OnPropertyChanged(nameof(this.SelectedFontFamiliesText));
                this.OnPropertyChanged(nameof(this.SelectedFontSizeText));
                this.OnPropertyChanged(nameof(this.SelectedWindowEffect));
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
    /// Gets or sets the selected profile's environment overrides as KEY=VALUE
    /// lines.
    /// </summary>
    public string SelectedEnvironmentText
    {
        get
        {
            if (this.selectedProfile?.EnvironmentOverrides is not { } env || env.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                env.Select(kv => kv.Key + "=" + kv.Value));
        }

        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.EnvironmentOverrides = ParseEnvironment(value);
            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's color scheme name. Empty =
    /// inherit default.
    /// </summary>
    public string SelectedColorSchemeName
    {
        get => this.selectedProfile?.ColorSchemeName ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.ColorSchemeName = string.IsNullOrEmpty(value) ? null : value;
            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's font families as a comma-separated list.
    /// </summary>
    public string SelectedFontFamiliesText
    {
        get => this.selectedProfile?.FontFamilies is { } f ? string.Join(", ", f) : string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                this.selectedProfile.FontFamilies = null;
            }
            else
            {
                this.selectedProfile.FontFamilies = value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's font size as a string. Empty =
    /// inherit default. Non-numeric input is ignored.
    /// </summary>
    public string SelectedFontSizeText
    {
        get => this.selectedProfile?.FontSize?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                this.selectedProfile.FontSize = null;
            }
            else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                this.selectedProfile.FontSize = parsed;
            }
            else
            {
                return;
            }

            this.OnPropertyChanged();
            this.Persist();
        }
    }

    /// <summary>
    /// Gets or sets the selected profile's window effect name. Empty =
    /// inherit default.
    /// </summary>
    public string SelectedWindowEffect
    {
        get => this.selectedProfile?.WindowEffect ?? string.Empty;
        set
        {
            if (this.selectedProfile is null)
            {
                return;
            }

            this.selectedProfile.WindowEffect = string.IsNullOrEmpty(value) ? null : value;
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
            EnvironmentOverrides = source.EnvironmentOverrides is null
                ? null
                : new Dictionary<string, string>(source.EnvironmentOverrides),
            ColorSchemeName = source.ColorSchemeName,
            FontFamilies = source.FontFamilies is null ? null : (string[])source.FontFamilies.Clone(),
            FontSize = source.FontSize,
            WindowEffect = source.WindowEffect,
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
        this.OnPropertyChanged(nameof(this.IsSelectedDefault));
        this.Persist();
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

    private static IReadOnlyDictionary<string, string>? ParseEnvironment(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in input.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..];
            if (key.Length > 0)
            {
                dict[key] = value;
            }
        }

        return dict.Count == 0 ? null : dict;
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
        this.SelectedProfile = this.Profiles.FirstOrDefault();
    }

    private void Persist()
    {
        this.store.Save(this.Profiles, this.defaultProfileId);
        App.ReloadProfiles();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
