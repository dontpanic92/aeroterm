# AeroTerm localization

This directory holds the user-facing string table for AeroTerm. Strings are
stored in [`.resx`](https://learn.microsoft.com/dotnet/framework/resources/creating-resource-files-for-desktop-apps)
files and are surfaced to AXAML and C# through a hand-written
strongly-typed accessor ([`Strings.cs`](./Strings.cs)), which in turn
delegates to [`AeroTerm/Services/LocalizationHost.cs`](../Services/LocalizationHost.cs).

## Files

| File | Purpose |
| --- | --- |
| `Strings.resx` | English (invariant) baseline. Every key used anywhere in the app **must** have an entry here. |
| `Strings.es.resx` | Spanish stub — deliberately partial (3 keys) to prove satellite-assembly generation works end-to-end. |
| `Strings.cs` | Hand-authored strongly-typed accessor, e.g. `Strings.SettingsTitle`. Exposes `ResourceManager` and one static property per key. |

The .NET SDK automatically picks up `*.resx` under the project directory
as `EmbeddedResource` and emits a satellite assembly
(`es/aeroterm.resources.dll`) for every culture-suffixed variant — no
manual `<EmbeddedResource>` item or `ResXFileCodeGenerator` metadata is
needed in the csproj.

## Adding a new string

1. Open `Strings.resx` and add a new `<data name="MyKey">` element with
   an English `<value>`. Keep keys in **PascalCase** and group them by
   purpose (the existing comments delimit Window titles, Buttons,
   AppearancePage, Palette, Confirm-close, etc.).
2. Add a matching static property to `Strings.cs`:
   ```csharp
   /// <summary>Gets the … caption.</summary>
   public static string MyKey => Get(nameof(MyKey));
   ```
3. Reference from AXAML:
   ```xml
   xmlns:strings="using:AeroTerm.Resources"
   …
   <Button Content="{x:Static strings:Strings.MyKey}" />
   ```
   Or from C#: `var caption = Strings.MyKey;`.
4. Optionally add the same key to culture-specific resx files
   (`Strings.es.resx`, `Strings.fr.resx`, …) to provide translations.
   Missing translations fall back to the English baseline automatically.

## Adding a new language

1. Copy `Strings.es.resx` to `Strings.<culture>.resx` where `<culture>`
   is a .NET culture identifier (`fr`, `pt-BR`, `zh-Hans`, …).
2. Translate the `<value>` elements. You don't have to translate every
   key — missing keys fall back to the English baseline via
   `ResourceManager`'s culture-parent chain.
3. Rebuild. The SDK will emit `<culture>/aeroterm.resources.dll`
   automatically; no csproj changes required.
4. Verify at runtime by setting `LocalizationHost.Culture = new
   CultureInfo("<culture>")` in a test, or by launching with
   `DOTNET_CLI_UI_LANGUAGE=<culture>` / a culture-aware startup hook.

## Format strings

When a string embeds runtime data, suffix the key with `Format` and use
`string.Format(CultureInfo.CurrentCulture, Strings.MyKeyFormat, …)` at
the call site. See `ConfirmCloseMessageFormat` / `ConfirmCloseDialog.cs`
for the canonical example.

## Scope of session 32 (`localization-scaffold`)

This pass established the **pattern**, not exhaustive coverage. The
strings below were extracted; everything else still lives as
inline literals and is tracked as follow-up work.

### Extracted (≥20 keys across 5 files)

* `MainWindow.axaml` — window `Title`.
* `SettingsWindow.axaml` — window `Title`.
* `CommandPaletteWindow.axaml` — window `Title`.
* `ConfirmCloseDialog.cs` — `Title`, body format, Close / Cancel buttons,
  "Close all tabs" automation name.
* `AppearancePage.axaml` — **fully converted**: every `GroupBox.Header`
  (Window / Font / Color Scheme / Bell / Scrollback / Tab Bar / General),
  the three primary `CheckBox.Content` values, and every font-list
  button caption (`Add... / Remove / Move Up / Move Down`).

### Deferred (follow-up task)

The following strings were **not** extracted by this scaffold pass and
should be swept in a follow-up:

* `AppearancePage.axaml` — the grey hint `TextBlock`s (ligature preview
  footnote, scrollback help text, tab-bar help text, Quake help / warning
  text), `TextBox` placeholder text, and automation-property strings
  (`AutomationProperties.Name/HelpText`).
* `KeybindingsPage.axaml`, `ProfilesPage.axaml`, `UpdatesPage.axaml` —
  all inline literals.
* `FontPickerWindow.axaml`, `QuakeWindow.axaml` — window titles and
  content.
* `MainWindow.axaml` — menu item text, tab-strip context menus, status
  messages.
* `CommandPaletteWindow.axaml` — placeholder and empty-state text; the
  palette-command captions registered by `PaletteCommandSource` (the
  three `Palette*` keys are defined in `Strings.resx` but the source
  still constructs captions from inline literals — a follow-up should
  replace those with `Strings.Palette*` lookups).
* `SettingsSearch` / `SettingsWindow.axaml` — search placeholder, "Clear
  search" automation name, per-page sidebar labels.
* All `ArgumentException` / log messages — these are developer-facing
  and intentionally kept in English per the usual .NET convention.
