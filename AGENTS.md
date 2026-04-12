# AeroTerm — Agent Guidelines

## Project Overview

AeroTerm provides reusable terminal infrastructure as NuGet packages:

- **AeroTerm.WindowEffects** — Cross-platform window blur, acrylic, mica, and transparency effects for Avalonia applications.
- **AeroTerm.Pty** — Cross-platform pseudo-terminal (PTY) library with ANSI/VT escape sequence parser, terminal buffer, and input encoder.

## Tech Stack

- **.NET 10** with latest C# language version
- **Avalonia 12** (WindowEffects package only)
- **Microsoft.Extensions.Logging.Abstractions** for logging
- **NUnit** for tests
- **StyleCop.Analyzers** for code style enforcement

## Build & Test

```bash
dotnet build aeroterm.slnx
dotnet test aeroterm.slnx
dotnet pack aeroterm.slnx
```

## Coding Conventions

- All files must have the GPLv2 copyright header (enforced by StyleCop)
- `TreatWarningsAsErrors` is enabled — zero warnings policy
- `Nullable` reference types are enabled — no nullable warnings allowed
- XML documentation is required on all public and internal members
- Use `this.` qualifier for instance members (StyleCop SA1101)
- Platform-specific code uses `RuntimeInformation.IsOSPlatform()` checks
- P/Invoke declarations are grouped in private `NativeMethods` nested classes

## Platform Support

- **Windows** — ConPTY, DWM blur/acrylic/mica
- **macOS** — forkpty via native C helper, NSWindow transparency/vibrancy
- **Linux** — forkpty via native C helper, basic compositor transparency
