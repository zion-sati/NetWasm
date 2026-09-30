# NetWasm documentation

NetWasm is a pre-1.0, closed-world CIL-to-WebAssembly toolchain. It is a smaller
.NET platform with an explicit CoreLib, runtime, and host boundary; it is not a
browser build of the desktop .NET runtime.

These pages are the user-facing reference for contracts implemented in this
repository.

## Start here

- [Size measurements and methodology](size-and-methodology.md) — reproduce the
  84,513-byte C# component and read the Rust comparison and JSON scenario limits.
- [NetWasm SDK quickstart](sdk-quickstart.md) — create an app or dual-target
  library, restore the pinned host tools through NuGet, then build, run and
  publish with ordinary `dotnet` commands.
- [Support status and roadmap](support-status.md) — the canonical inventory of
  implemented, conditional, partially qualified, in-progress, deferred,
  externally blocked, and intentionally unsupported capabilities.
- [Manual build and deployment](cli-build-deployment.md) — the low-level
  source-build/compiler reference, output artifacts, and host entry points.
- [Targets and output formats](targets-and-outputs.md) — wasm32/wasm64 core
  modules, Component Model packaging, `cm32p2`/`cm64p2`, and WASI version
  boundaries.
- [Compiler diagnostics](diagnostics.md) — every current
  `NetWasm.Compiler.Core.DiagnosticCode`, including cause, exact scope, and
  corrective action.
- [CoreLib and runtime compatibility](corelib-runtime.md) — the supported
  platform boundary, intentionally unsupported surfaces, and license split.
- [Language, CLI, and .NET behavior differences](spec-deviations.md) — the
  durable ledger for deliberate profile differences and
  specification-permitted results.
- [Known gaps and boundaries](gaps.md) — a concise route to the canonical
  status inventory and its current external/tooling boundaries.
- [Licensing guide](licensing.md) — the NetWasm Community License 1.0 for
  compiler/tooling, MIT CoreLib/runtime/framework code, Sponsors commercial
  tiers, generated-output freedom, and upstream-notice exceptions.
- [Documentation maintenance](maintenance.md) — same-change update rules and
  the parity/navigation verifier.

## Concepts and reference

- [Manual source-build walkthrough](../QUICKSTART.md) — a complete low-level
  raw-core and Component Model example, not the package-consumer quickstart.
- [Compiler architecture](ARCHITECTURE.md) — compiler composition,
  reachability, layout, and Wasm emission.
- [Expression-tree execution profile](expression-trees.md): the bounded,
  interpreter-only executable node and delegate matrix.
- [Timezone assets](timezone-assets.md) — optional timezone deployment and
  runtime capability selection.

## Status and compatibility

The supported profile is usable today and covered by its published qualification.
Until 1.0, CIL support, CoreLib APIs, runtime ABI, generated manifests, and build
output can change between minor releases. A desktop-targeted NuGet package is
compatible only when its API and CIL dependencies fit the documented NetWasm
profile and the desired output boundary.

Desktop behavior is not automatically the conformance oracle. Consult the
[behavior-differences ledger](spec-deviations.md) for intentional profile
choices and cases where C#, ECMA-335, or a .NET API permits more than one
result.

For the current answer to "is this supported?", start with the
[support status and roadmap](support-status.md), then use the target and
CoreLib references to verify the exact application boundary.

The compiler, linker, optimizer, compiler/build tooling, debugger, and
IDE/browser developer tooling are covered by the NetWasm Community License 1.0.
`NetWasm.CoreLib`, `NetWasm.Runtime`, templates, generated support code, and
adjacent framework ports are MIT, subject to preserved upstream notices. See
the [licensing guide](licensing.md).
