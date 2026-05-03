# AeroTerm.Theme

AeroTerm.Theme is a unified Fluent-flavored Avalonia theme for applications that want AeroTerm's modern terminal UI foundation without depending on `Avalonia.Themes.Fluent` or `Avalonia.Themes.Simple`. It bundles Light and Dark variants, accent-aware color resources, typography, metrics, motion tokens, and control themes into one reusable package.

The theme is token-driven: templates consume named brushes instead of hard-coded colors, and those brushes resolve through Avalonia theme dictionaries so switching `ActualThemeVariant` automatically updates the UI. Consumers can use the bundled defaults as-is or override individual resource keys at application, window, or control scope.

## Installation / consumption

Reference the `AeroTerm.Theme` NuGet package, then add the theme to `Application.Styles`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="using:AeroTerm.Theme"
             x:Class="MyApp.App">
  <Application.Styles>
    <theme:AeroTermTheme />
  </Application.Styles>
</Application>
```

## Token reference

All brush tokens are defined in `Tokens/Brushes.axaml` and are safe to consume with `{DynamicResource ...}`.

### Surfaces

- `SurfaceBackgroundBrush` — Main application background surface.
- `SurfaceLayer1Brush` — First raised layer for panels and cards.
- `SurfaceLayer2Brush` — Second raised layer for nested or emphasized panels.
- `SurfaceOverlayBrush` — Floating overlay surface for popups and transient UI.
- `SurfaceScrimBrush` — Modal scrim or dimming layer behind overlays.

### Strokes

- `StrokeDefaultBrush` — Standard divider and outline stroke.
- `StrokeMutedBrush` — Subtle divider and low-emphasis outline stroke.
- `StrokeFocusBrush` — Keyboard focus and high-visibility focus stroke.
- `StrokeAccentBrush` — Accent-colored stroke for highlighted outlines.

### Text

- `TextPrimaryBrush` — Primary foreground text.
- `TextSecondaryBrush` — Secondary foreground text for supporting labels.
- `TextTertiaryBrush` — Low-emphasis foreground text.
- `TextDisabledBrush` — Disabled foreground text.
- `TextOnAccentBrush` — Text displayed on accent-colored fills.
- `TextLinkBrush` — Hyperlink and link-like foreground text.

### Accent

- `AccentPrimaryBrush` — Primary accent fill.
- `AccentSecondaryBrush` — Secondary accent fill.
- `AccentMutedBrush` — Low-emphasis accent fill.
- `AccentPressedBrush` — Pressed-state accent fill.
- `AccentDisabledBrush` — Disabled-state accent fill.

### Control fill/border

- `ControlFillRestBrush` — Default control background fill.
- `ControlFillHoverBrush` — Control background fill on pointer hover.
- `ControlFillPressedBrush` — Control background fill while pressed.
- `ControlFillDisabledBrush` — Disabled control background fill.
- `ControlFillSubtleHoverBrush` — Subtle hover fill for low-chrome controls.
- `ControlFillSubtlePressedBrush` — Subtle pressed fill for low-chrome controls.
- `ControlBorderRestBrush` — Default control border stroke.
- `ControlBorderHoverBrush` — Control border stroke on pointer hover.
- `ControlBorderFocusBrush` — Control border stroke for focused controls.
- `ControlBorderDisabledBrush` — Disabled control border stroke.

### Status

- `SuccessFillBrush` — Success-state background fill.
- `SuccessForegroundBrush` — Success-state foreground text or icon brush.
- `WarningFillBrush` — Warning-state background fill.
- `WarningForegroundBrush` — Warning-state foreground text or icon brush.
- `DangerFillBrush` — Error or destructive-state background fill.
- `DangerForegroundBrush` — Error or destructive-state foreground text or icon brush.
- `InfoFillBrush` — Informational background fill.
- `InfoForegroundBrush` — Informational foreground text or icon brush.

### Selection

- `SelectionFillBrush` — Active selection background fill.
- `SelectionFillInactiveBrush` — Inactive selection background fill.
- `SelectionForegroundBrush` — Foreground brush for selected content.
- `ListItemHoverBrush` — List item hover background fill.
- `ListItemPressedBrush` — List item pressed background fill.
- `ListItemSelectedBrush` — Selected list item background fill.
- `ListItemSelectedHoverBrush` — Selected list item hover background fill.

### Title bar

- `TitleBarForegroundBrush` — Title bar foreground text and icon brush.
- `TitleBarButtonHoverBrush` — Caption button hover background fill.
- `TitleBarButtonPressedBrush` — Caption button pressed background fill.
- `TitleBarCloseHoverBrush` — Close button hover background fill.
- `TitleBarClosePressedBrush` — Close button pressed background fill.

### Palette

- `PaletteBackground` — Command palette background surface.
- `PaletteForeground` — Command palette primary foreground brush.
- `PaletteBorder` — Command palette border stroke.
- `PaletteSelection` — Command palette selected item fill.
- `PaletteMuted` — Command palette muted foreground brush.

### Search overlay

- `SearchOverlayBackground` — Search overlay background surface.
- `SearchOverlayForeground` — Search overlay primary foreground brush.
- `SearchOverlayBorder` — Search overlay border stroke.
- `SearchOverlayMuted` — Search overlay muted foreground brush.
- `SearchOverlayButtonHover` — Search overlay button hover fill.
- `SearchOverlayButtonPressed` — Search overlay button pressed fill.
- `SearchOverlayToggleOn` — Search overlay active toggle fill.

### Tab strip

- `TabStripDividerBrush` — Tab strip divider stroke.
- `TabStripCloseHoverBrush` — Tab close button hover fill.
- `TabStripActiveAccentBrush` — Accent indicator for the active tab.
- `TabStripForegroundBrush` — Tab strip primary foreground brush.
- `TabStripMutedForegroundBrush` — Tab strip muted foreground brush.

## Customization

Override tokens by defining the same `x:Key` at a higher resource scope, such as `Application.Resources`, `Window.Resources`, or a control's `Resources`. Prefer `DynamicResource` in consuming styles so runtime theme changes and overrides continue to flow through.

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="using:AeroTerm.Theme">
  <Application.Resources>
    <SolidColorBrush x:Key="AccentPrimaryBrush" Color="#FF8A2BE2" />
    <SolidColorBrush x:Key="ControlBorderFocusBrush" Color="#FF8A2BE2" />
  </Application.Resources>

  <Application.Styles>
    <theme:AeroTermTheme />
  </Application.Styles>
</Application>
```

## Theme variants

AeroTerm.Theme provides Light and Dark resources through Avalonia `ThemeDictionaries`. Set `RequestedThemeVariant="Light"` or `RequestedThemeVariant="Dark"` on `Application` (or another theme variant scope) to force a variant.

Use `RequestedThemeVariant="Default"`, or omit the property, to let Avalonia follow the operating system preference automatically.

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:theme="using:AeroTerm.Theme"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <theme:AeroTermTheme />
  </Application.Styles>
</Application>
```

## Native menu and dropdown wrappers

`AeroTerm.Theme.Controls.NativeMenuFlyout` and `NativeContextMenu` provide an Avalonia-friendly wrapper for context menus and flyout menus:

- macOS uses real AppKit `NSMenu` / `NSMenuItem` instances, so the operating system supplies the current native menu styling, including Liquid Glass on supported macOS versions.
- Windows and Linux use the existing Avalonia `MenuFlyout`, `ContextMenu`, `MenuItem`, and AeroTerm.Theme menu templates.

Windows intentionally does not include WinUI 3 / Windows App SDK menu support in this package. WinUI 3 requires package/runtime dependencies and XAML hosting setup that cannot be implemented as simple native interop like AppKit menus. A future optional companion package can add that path without making the base theme package heavier.

```xml
<Button xmlns:menus="using:AeroTerm.Theme.Controls"
        Content="Profiles">
  <Button.Flyout>
    <menus:NativeMenuFlyout>
      <menus:NativeMenuItem Header="Default" />
      <menus:NativeMenuSeparator />
      <menus:NativeMenuItem Header="Manage profiles…" />
    </menus:NativeMenuFlyout>
  </Button.Flyout>
</Button>
```

For context menus, attach `NativeContextMenu.Menu` to the target control:

```xml
<Border xmlns:menus="using:AeroTerm.Theme.Controls">
  <menus:NativeContextMenu.Menu>
    <menus:NativeContextMenu>
      <menus:NativeMenuItem Header="Duplicate tab" />
      <menus:NativeMenuItem Header="Close tab" />
    </menus:NativeContextMenu>
  </menus:NativeContextMenu.Menu>
</Border>
```

`NativeDropdown` uses the same platform menu path for its dropdown list. On macOS, choices are displayed with the AppKit native menu implementation; on Windows and Linux, choices are displayed with AeroTerm.Theme's Avalonia menu fallback.

```xml
<menus:NativeDropdown xmlns:menus="using:AeroTerm.Theme.Controls"
                      PlaceholderText="Choose profile">
  <menus:NativeDropdownItem Content="Default" Value="default" />
  <menus:NativeDropdownItem Content="Admin" Value="admin" />
</menus:NativeDropdown>
```

Use `SelectedIndex`, `SelectedItem`, `SelectedValue`, and `SelectionChanged` to observe or update selection from code or bindings.

## Native message boxes

`AeroTerm.Theme.Controls.NativeMessageBox` provides small modal message boxes with native presentation where practical:

- macOS uses AppKit `NSAlert`.
- Windows and Linux use an AeroTerm-themed Avalonia modal window.
- Supported variants are OK-only and Yes/No.

```csharp
await NativeMessageBox.ShowOkAsync(owner, "Saved", "Settings were saved.");

NativeMessageBoxResult result = await NativeMessageBox.ShowYesNoAsync(
    owner,
    "Close window?",
    "2 tabs are open. Close them all?");

if (result == NativeMessageBoxResult.Yes)
{
    owner.Close();
}
```

Button labels default to English `OK`, `Yes`, and `No`. Applications that localize their own UI can pass localized labels:

```csharp
await NativeMessageBox.ShowYesNoAsync(
    owner,
    title,
    message,
    yesText: "Close",
    noText: "Cancel");
```

## Compatibility keys

The theme also provides legacy compatibility resources used by existing AeroTerm code and older consumers:

- `SystemAccentColor`
- `SystemAccentColorBrush`
- `ThemeBorderLowColor`
- `ThemeBorderMidColor`

New code should prefer the tokenized accent, stroke, and border brush keys listed above.

## License

GPL-2.0-or-later. See `LICENSE` at the repository root.
