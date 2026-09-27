# NetWasm documentation

NetWasm is an experimental, closed-world CIL-to-WebAssembly toolchain. It is
a smaller .NET platform with an explicit CoreLib, runtime, and host boundary;
it is not a browser build of the desktop .NET runtime.

These pages are the user-facing reference for contracts implemented in this
repository.

## Start here

- [Size measurements and methodology](size-and-methodology.md) — reproduce the
  84,515-byte C# component and read the Rust comparison and JSON scenario limits.
- [NetWasm SDK quickstart](sdk-quickstart.md) — install the pinned Emscripten
  SDK that supplies Node.js 24+ and LLD 24+, create an app or dual-target
  library, then build, run and publish with ordinary `dotnet` commands.
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
- [Generated STJ deserialization triage ledger](diagnostics/stj-generated-deserialization-triage.md)
  — hypotheses, ruled-out causes, root cause, and verification for the resolved
  source-generated JSON compiler defect.
- [Generated STJ output-root lifetime triage ledger](diagnostics/stj-generated-shape-root-lifetime-triage.md)
  — the separate managed-address output lifetime defect and its precise-GC fix.
- [Precise runtime-root enumeration triage ledger](diagnostics/runtime-root-enumeration-capacity-triage.md)
  — the BDWGC root-registration capacity defect exposed by the expanded STJ
  closed world and its typed-root enumerator fix.
- [CoreLib and runtime compatibility](corelib-runtime.md) — the supported
  platform boundary, intentionally unsupported surfaces, and license split.
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
- [Compiler architecture](compiler-architecture.md) — compiler composition,
  reachability, layout, and Wasm emission.
- [Timezone assets](timezone-assets.md) — optional timezone deployment and
  runtime capability selection.

## Status and compatibility

The current implementation is suitable for experiments and early integration,
not production deployment. CIL support, CoreLib APIs, runtime ABI, generated
manifests, and build output can change. A desktop-targeted NuGet package is
compatible only when its API and CIL dependencies fit the documented NetWasm
profile and the desired output boundary.

For the current answer to "is this supported?", start with the
[support status and roadmap](support-status.md), then use the target and
CoreLib references to verify the exact application boundary.

The compiler, linker, optimizer, compiler/build tooling, debugger, and
IDE/browser developer tooling are covered by the NetWasm Community License 1.0.
`NetWasm.CoreLib`, `NetWasm.Runtime`, templates, generated support code, and
adjacent framework ports are MIT, subject to preserved upstream notices. See
the [licensing guide](licensing.md).
