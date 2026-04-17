# AeroTerm.Pty

Cross-platform pseudo-terminal (PTY) library with an ANSI/VT escape sequence
parser. Provides ConPTY (Windows) and `forkpty` (macOS/Linux) backends, a full
VT state machine, terminal buffer with scrollback and alternate screen, mouse
tracking modes, unicode width tables, and an input encoder.

## Install

```bash
dotnet add package AeroTerm.Pty
```

## Documentation

See the [AeroTerm documentation site](https://github.com/dontpanic92/aeroterm)
(`docs/packages/pty.md`) for API usage and examples.

## License

GPL-2.0-or-later. See [LICENSE](https://github.com/dontpanic92/aeroterm/blob/master/LICENSE).
