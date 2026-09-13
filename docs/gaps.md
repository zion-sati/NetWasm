# Known gaps and boundaries

The [support status and roadmap](support-status.md) is the canonical public
inventory of complete/supported, Conditional, partially qualified, in-progress,
deferred, externally blocked, and intentionally unsupported capabilities.

The most important current boundaries are the preview support commitment,
selective managed-library coverage, and the lack of wasm64 Component output.
There is no member-by-member CoreLib catalogue. wasm64
core modules and raw-core integration work, but Component packaging reports
`NW1010` while the external `cm64p2`/Memory64 Component toolchain is
unavailable. Components target WASI 0.2; a wasm32 WASI 0.3 transport
evolution neither solves nor waits on that independent Memory64 limitation.

The first preview also has no general managed file/directory, raw-socket,
subprocess, or multi-threaded runtime surface. The exact supported exceptions
and missing APIs are listed in the
[WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).

These are current boundaries, not release promises. See [targets and output
formats](targets-and-outputs.md) and the [CLI/build/deployment guide](cli-build-deployment.md)
for implemented alternatives.
