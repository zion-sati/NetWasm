# Known gaps and boundaries

The [support status and roadmap](support-status.md) is the canonical public
inventory of complete/supported, Conditional, partially qualified, in-progress,
deferred, externally blocked, and intentionally unsupported capabilities.

The most important current boundaries are the preview support commitment,
selective managed-library coverage, and the lack of wasm64 Component output.
There is no member-by-member CoreLib catalogue. wasm64
core modules and raw-core integration work, but Component packaging reports
`NW1010` while the external `cm64p2`/Memory64 Component toolchain is
unavailable. Components target WASI 0.3, which neither solves nor waits on
that independent Memory64 limitation.

The first preview also has no general managed file/directory, raw-socket,
subprocess, or multi-threaded runtime surface. The exact supported exceptions
and missing APIs are listed in the
[WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).

These are current boundaries, not release promises. See [targets and output
formats](targets-and-outputs.md) and the [CLI/build/deployment guide](cli-build-deployment.md)
for implemented alternatives.

## Known problems

### Release builds of large programs need a lot of memory

Release builds optimize the whole program with Binaryen's `wasm-opt`, which holds all of it in memory and by
default runs one thread per CPU core. Most programs peak well under 2 GB, but one that compiles large libraries can
need much more: a program whose core module is about 50 MB peaks at about 16 GB on a 16-thread machine. Debug builds
skip this optimization.

If a Release build runs out of memory, give `wasm-opt` fewer threads with `BINARYEN_CORES`. With four threads the
same program peaks at about 9 GB, and the optimization takes about 60% longer. With one thread it peaks at about
6 GB, and the whole build takes about five times as long.

```bash
BINARYEN_CORES=4 dotnet build -c Release
```

```powershell
$env:BINARYEN_CORES = 4; dotnet build -c Release
```
