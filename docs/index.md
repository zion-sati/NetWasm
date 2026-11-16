# NetWasm documentation

NetWasm is a pre-1.0, closed-world CIL-to-WebAssembly toolchain. It is a smaller
.NET platform with an explicit CoreLib, runtime, and host boundary; it is not a
browser build of the desktop .NET runtime.

These pages are the user-facing reference for contracts implemented in this
repository.

## Start here

- [Changelog](changelog.md): every release, newest first, with its release notes.
- [Size measurements and methodology](size-and-methodology.md): reproduce the
  28,383-byte C# component and read the Rust, Kotlin, TinyGo, Blazor and JSON
  comparison boundaries.
- [Performance compared with Mono AOT and desktop .NET](performance.md):
  speed, size and memory on allocation and garbage-collection workloads, with
  versions, settings and reproduction.
- [Micro-benchmark results](micro-benchmarks.md): every dotnet/performance
  micro-benchmark case on NetWasm, Mono AOT and desktop .NET.
- [NetWasm SDK quickstart](sdk-quickstart.md): create an app or dual-target
  library, restore the pinned host tools through NuGet, then build, run and
  publish with ordinary `dotnet` commands.
- [JavaScript interop](javascript-interop.md): use `[JSImport]` and
  `[JSExport]`, publish C# exports as a page-hosted JavaScript module, and pass
  callbacks, tasks, typed arrays and JavaScript objects within the supported ABI.
- [DOM and Canvas from C#](dom-and-canvas.md): read and write the page, handle
  events, and draw C#-computed pixels to a Canvas every animation frame.
- [Static native C interop](static-native-interop.md): link Wasm `.a`
  archives with `[LibraryImport]` and expose managed callbacks with
  `[UnmanagedCallersOnly]`.
- [WIT-to-C# bindings](wit-bindings.md): run the standalone binding tool,
  select a world, understand the generated model, and match it to the runtime
  boundary.
- [Support status](support-status.md): the canonical inventory of
  implemented, conditional, partially qualified, in-progress, deferred,
  externally blocked, and intentionally unsupported capabilities.
- [Roadmap to NetWasm 1.0](roadmap.md): the release gates for the stable
  platform contract, with current support kept separate from future intent.
- [Manual build and deployment](cli-build-deployment.md): the low-level
  source-build/compiler reference, output artifacts, and host entry points.
- [Targets and output formats](targets-and-outputs.md): wasm32/wasm64 core
  modules, Component Model packaging, `cm32p2`/`cm64p2`, and WASI version
  boundaries.
- [C# Web Workers](web-workers.md): publish WIT workers or JavaScript modules
  in a worker, call the generated JavaScript client, exchange notifications, and
  manage worker lifecycle.
- [Compiler diagnostics](diagnostics.md): every current
  `NetWasm.Compiler.Core.DiagnosticCode`, including cause, exact scope, and
  corrective action.
- [Popular NuGet packages](package-compatibility.md): how the 100 most-downloaded
  .NET Standard packages run on NetWasm, what stops the others, and replacements
  that run.
- [.NET Standard libraries](netstandard-libraries.md): which packages restore, what
  throws when a library reaches something NetWasm does not provide, and how library
  authors avoid reflection, type-name probes and platform-specific paths.
- [CoreLib and runtime compatibility](corelib-runtime.md): the supported
  platform boundary, intentionally unsupported surfaces, and license split.
- [Choosing a garbage collector](runtime-garbage-collection.md): Compact's
  size-first default, NetWasmGC and Bump, and build-time selection.
- [Language, CLI, and .NET behavior differences](spec-deviations.md): the
  durable ledger for deliberate profile differences and
  specification-permitted results.
- [Known gaps and boundaries](gaps.md): a concise route to the canonical
  status inventory and its current external/tooling boundaries.
- [Licensing guide](licensing.md): the NetWasm Community License 1.0 for the
  compiler and the tools that create your deployable package, MIT for
  everything else including CoreLib, runtime, JavaScript host and framework
  code, Sponsors commercial tiers, generated-output freedom, and
  upstream-notice exceptions.
- [Documentation maintenance](maintenance.md): same-change update rules and
  the parity/navigation verifier.

## Concepts and reference

- [Bounded custom attribute queries](custom-attributes.md): candidate API shapes,
  static type provenance, user-defined attributes, retention and diagnostics.
- [Manual source-build walkthrough](../QUICKSTART.md): a complete low-level
  raw-core and Component Model example, not the package-consumer quickstart.
- [Compiler architecture](ARCHITECTURE.md): compiler composition,
  reachability, layout, and Wasm emission.
- [Expression-tree execution profile](expression-trees.md): the bounded,
  interpreter-only executable node and delegate matrix.
- [Timezone assets](timezone-assets.md): choose the time zones an application
  ships, and how hosts load them.

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

The compiler, linker, optimizer, and the surrounding tools that create a
user's deployable package are covered by the NetWasm Community License 1.0.
`NetWasm.CoreLib`, `NetWasm.Runtime`, the JavaScript host, templates, generated
support code, adjacent framework ports, and everything else are MIT, subject to
preserved upstream notices. See
the [licensing guide](licensing.md).
