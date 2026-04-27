# AeroTerm.WindowEffects

Cross-platform window effects library for [Avalonia](https://avaloniaui.net/)
applications. Provides blur, acrylic, mica, and transparency management with
platform-specific interop for Windows (DWM) and macOS (`NSWindow`), plus a
best-effort Linux compositor path.

## macOS Liquid Glass (macOS 26+)

`BlurType.LiquidGlass` enables Apple's Liquid Glass material on macOS 26
(Tahoe) and later. The library installs an `NSGlassEffectView` as the
back-most subview of the window's `contentView` via Objective-C runtime
calls — the consuming app does **not** need to be linked against the
Xcode 26 SDK; runtime macOS 26 is sufficient. On older macOS versions
(or off macOS) the effect silently falls back to a plain transparent
window and a single info-level log message is emitted.

## Install

```bash
dotnet add package AeroTerm.WindowEffects
```

## Documentation

See the [AeroTerm documentation site](https://github.com/dontpanic92/aeroterm)
(`docs/packages/window-effects.md`) for API usage and examples.

## License

GPL-2.0-or-later. See [LICENSE](https://github.com/dontpanic92/aeroterm/blob/master/LICENSE).
