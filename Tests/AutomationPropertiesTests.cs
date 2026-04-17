// <copyright file="AutomationPropertiesTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;

/// <summary>
/// Parses the .axaml sources as XML and asserts that critical interactive
/// controls (the ones end-users reach via keyboard / screen reader)
/// carry an accessible name — either literal text content or an
/// <c>AutomationProperties.Name</c> / <c>AutomationProperties.LabeledBy</c>
/// attribute.
/// </summary>
/// <remarks>
/// The test inspects the XAML source directly rather than instantiating
/// the controls because the latter would require the full Avalonia
/// headless stack for every window — overkill for a structural check.
/// Only controls with glyph-only content (×, ⚙, arrows, etc.) require
/// an explicit <c>AutomationProperties.Name</c>; text buttons like
/// <c>Content="OK"</c> are considered self-labelling.
/// </remarks>
[TestFixture]
public class AutomationPropertiesTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "aeroterm.slnx")))
            {
                dir = dir.Parent;
            }

            Assert.That(dir, Is.Not.Null, "Could not locate aeroterm.slnx");
            return Path.Combine(dir!.FullName, "AeroTerm");
        }
    }

    /// <summary>
    /// The four custom title-bar buttons in <c>MainWindow.axaml</c> use
    /// glyph-only content and therefore must carry an explicit
    /// <c>AutomationProperties.Name</c>.
    /// </summary>
    [Test]
    public void MainWindowTitleBarButtonsHaveAutomationName()
    {
        var doc = Load("MainWindow.axaml");
        string[] required = { "SettingsButton", "MinimizeButton", "MaximizeButton", "CloseButton" };
        foreach (var name in required)
        {
            var btn = FindByName(doc, "Button", name);
            Assert.That(btn, Is.Not.Null, $"Button Name='{name}' not found in MainWindow.axaml");
            Assert.That(
                HasAutomationName(btn!),
                Is.True,
                $"Button '{name}' missing AutomationProperties.Name");
        }
    }

    /// <summary>
    /// The search overlay's glyph toggles and buttons must carry
    /// <c>AutomationProperties.Name</c>.
    /// </summary>
    [Test]
    public void SearchOverlayButtonsHaveAutomationName()
    {
        var doc = Load("Controls/SearchOverlay.axaml");
        string[] requiredToggles = { "CaseToggle", "RegexToggle", "WordToggle" };
        foreach (var name in requiredToggles)
        {
            var el = FindByName(doc, "ToggleButton", name);
            Assert.That(el, Is.Not.Null, $"ToggleButton '{name}' not found");
            Assert.That(HasAutomationName(el!), Is.True, $"ToggleButton '{name}' missing AutomationProperties.Name");
        }

        string[] requiredButtons = { "PrevButton", "NextButton", "CloseButton" };
        foreach (var name in requiredButtons)
        {
            var el = FindByName(doc, "Button", name);
            Assert.That(el, Is.Not.Null, $"Button '{name}' not found");
            Assert.That(HasAutomationName(el!), Is.True, $"Button '{name}' missing AutomationProperties.Name");
        }
    }

    /// <summary>
    /// The Settings window sidebar search box and list must both be
    /// labelled for screen readers.
    /// </summary>
    [Test]
    public void SettingsWindowSidebarHasAutomationMetadata()
    {
        var doc = Load("Dialogs/SettingsWindow.axaml");
        var search = FindByName(doc, "TextBox", "SearchBox");
        Assert.That(search, Is.Not.Null);
        Assert.That(HasAutomationName(search!), Is.True, "SearchBox missing AutomationProperties.Name");

        var list = FindByName(doc, "ListBox", "PageList");
        Assert.That(list, Is.Not.Null);
        Assert.That(HasAutomationName(list!), Is.True, "PageList missing AutomationProperties.Name");
    }

    /// <summary>
    /// Every button inside each critical XAML file either has visible
    /// text content via <c>Content="…"</c> or an explicit
    /// <c>AutomationProperties.Name</c>. Empty, template-typed, or
    /// glyph-only buttons must opt in.
    /// </summary>
    [Test]
    public void AllButtonsInAuditedFilesHaveAccessibleName()
    {
        string[] files =
        {
            "MainWindow.axaml",
            "Dialogs/SettingsWindow.axaml",
            "Dialogs/AppearancePage.axaml",
            "Dialogs/UpdatesPage.axaml",
            "Dialogs/KeybindingsPage.axaml",
            "Dialogs/ProfilesPage.axaml",
            "Dialogs/FontPickerWindow.axaml",
            "Dialogs/CommandPaletteWindow.axaml",
            "Controls/SearchOverlay.axaml",
        };

        var missing = new List<string>();
        foreach (var rel in files)
        {
            var doc = Load(rel);
            foreach (var btn in doc.Descendants(Av + "Button"))
            {
                if (HasTextContent(btn) || HasAutomationName(btn) || HasLabeledBy(btn))
                {
                    continue;
                }

                missing.Add($"{rel}: Button at line {((IXmlLineInfo)btn).LineNumber}");
            }
        }

        Assert.That(missing, Is.Empty, "Buttons without an accessible name:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Every <c>TextBox</c> in the profile form must be addressable by
    /// name or label, since they are all unlabelled by default.
    /// </summary>
    [Test]
    public void ProfilesPageTextBoxesAreLabeled()
    {
        var doc = Load("Dialogs/ProfilesPage.axaml");
        var missing = new List<string>();
        foreach (var tb in doc.Descendants(Av + "TextBox"))
        {
            if (HasAutomationName(tb) || HasLabeledBy(tb))
            {
                continue;
            }

            missing.Add(((IXmlLineInfo)tb).LineNumber.ToString());
        }

        Assert.That(missing, Is.Empty, "Unlabelled TextBoxes at lines: " + string.Join(",", missing));
    }

    /// <summary>
    /// The Appearance page uses inline TextBlock+Slider/NumericUpDown
    /// rows; those input controls must point at their label via
    /// <c>AutomationProperties.LabeledBy</c>.
    /// </summary>
    [Test]
    public void AppearancePageInputsHaveLabeledByOrName()
    {
        var doc = Load("Dialogs/AppearancePage.axaml");
        foreach (var el in doc.Descendants().Where(e =>
            e.Name == Av + "Slider" || e.Name == Av + "NumericUpDown" || e.Name == Av + "ComboBox"))
        {
            Assert.That(
                HasAutomationName(el) || HasLabeledBy(el),
                Is.True,
                $"{el.Name.LocalName} at line {((IXmlLineInfo)el).LineNumber} needs AutomationProperties.Name or LabeledBy");
        }
    }

    /// <summary>
    /// The command palette query/results must be labelled — screen
    /// readers rely on them to distinguish the palette from the terminal
    /// view underneath.
    /// </summary>
    [Test]
    public void CommandPaletteControlsHaveAutomationName()
    {
        var doc = Load("Dialogs/CommandPaletteWindow.axaml");
        var query = FindByName(doc, "TextBox", "QueryTextBox");
        Assert.That(query, Is.Not.Null);
        Assert.That(HasAutomationName(query!), Is.True);

        var results = FindByName(doc, "ListBox", "Results");
        Assert.That(results, Is.Not.Null);
        Assert.That(HasAutomationName(results!), Is.True);
    }

    /// <summary>
    /// The font picker's filter box and list must be labelled.
    /// </summary>
    [Test]
    public void FontPickerControlsHaveAutomationName()
    {
        var doc = Load("Dialogs/FontPickerWindow.axaml");
        int labeledInputs = doc.Descendants(Av + "TextBox").Count(HasAutomationName)
            + doc.Descendants(Av + "ListBox").Count(HasAutomationName);
        Assert.That(labeledInputs, Is.GreaterThanOrEqualTo(2), "Expected at least 2 labelled inputs");
    }

    /// <summary>
    /// Tab order on Settings window must be set explicitly on search
    /// box, sidebar list, content viewer and the OK/Cancel buttons.
    /// </summary>
    [Test]
    public void SettingsWindowHasExplicitTabOrder()
    {
        var doc = Load("Dialogs/SettingsWindow.axaml");
        int countWithTabIndex = doc.Descendants()
            .Count(e => e.Attribute("TabIndex") is not null);
        Assert.That(countWithTabIndex, Is.GreaterThanOrEqualTo(5), "Expected explicit TabIndex on settings controls");
    }

    /// <summary>
    /// AccessKey mnemonics (<c>_OK</c>, <c>_Cancel</c>) are required on
    /// the settings dialog's primary buttons so keyboard-only users can
    /// dismiss the dialog with Alt+letter.
    /// </summary>
    [Test]
    public void SettingsWindowButtonsHaveAccessKeyMnemonics()
    {
        var doc = Load("Dialogs/SettingsWindow.axaml");
        var buttons = doc.Descendants(Av + "Button")
            .Select(b => (string?)b.Attribute("Content"))
            .Where(c => c is not null)
            .ToList();

        Assert.That(buttons.Any(c => c!.Contains('_') && c.EndsWith("OK", StringComparison.Ordinal)), Is.True);
        Assert.That(buttons.Any(c => c!.Contains('_') && c.EndsWith("Cancel", StringComparison.Ordinal)), Is.True);
    }

    private static XDocument Load(string relativePath)
    {
        var path = Path.Combine(AppDir, relativePath);
        Assert.That(File.Exists(path), Is.True, $"Missing XAML: {path}");
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    }

    private static XElement? FindByName(XDocument doc, string localName, string name)
    {
        return doc.Descendants(Av + localName)
            .FirstOrDefault(e => (string?)e.Attribute(Xaml + "Name") == name
                || (string?)e.Attribute("Name") == name);
    }

    private static bool HasTextContent(XElement el)
    {
        var content = (string?)el.Attribute("Content");
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // Treat single-character / symbolic glyphs as NOT self-labelling.
        // Strip the AccessKey underscore if present.
        string trimmed = content.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
        if (trimmed.Length <= 2)
        {
            return false;
        }

        // Any alphabetic content counts.
        foreach (char c in trimmed)
        {
            if (char.IsLetter(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAutomationName(XElement el)
    {
        return el.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.Name"
            && !string.IsNullOrWhiteSpace(a.Value));
    }

    private static bool HasLabeledBy(XElement el)
    {
        return el.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.LabeledBy"
            && !string.IsNullOrWhiteSpace(a.Value));
    }
}
