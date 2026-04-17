# AeroTerm.Benchmarks

BenchmarkDotNet micro-benchmarks for the hot paths in `AeroTerm.Pty` —
`VtParser` (ANSI/VT escape parsing) and `TerminalBuffer` (the structure
the renderer reads every frame).

## Running

Run the full suite in Release:

```sh
dotnet run -c Release --project AeroTerm.Benchmarks -- --filter '*'
```

For a fast smoke-test (completes in ~1 minute), use the built-in
`Short` job which lowers the warmup/measurement counts:

```sh
dotnet run -c Release --project AeroTerm.Benchmarks -- --filter '*' --job Short
```

Run a single class or method by tightening the filter:

```sh
dotnet run -c Release --project AeroTerm.Benchmarks -- \
  --filter '*VtParserBenchmarks*' --job Short
```

Results are written to `BenchmarkDotNet.Artifacts/` under the current
working directory.

## Benchmarks

- **`VtParserBenchmarks`** feeds ~64 KiB synthetic buffers through a
  fresh `VtParser` + `TerminalBuffer` pair: plain text, SGR-heavy
  output, cursor addressing, OSC 8 hyperlinks, OSC 133 shell
  integration, multi-codepoint grapheme clusters, and a representative
  mix.
- **`TerminalBufferBenchmarks`** exercises `PutCluster` for ASCII and
  wide/ZWJ clusters, and measures `Resize` (which dispatches to
  `ResizeReflowPrimary`) with the scrollback ring filled to the
  configured limit.

## Notes

- The project sets `IsPackable=false` — it's an executable harness,
  not a shipped package.
- BenchmarkDotNet is intentionally the only third-party dependency.
- This project is excluded from `dotnet test` automatically because it
  is not an NUnit test project.
