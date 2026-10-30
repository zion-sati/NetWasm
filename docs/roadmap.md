# Roadmap to NetWasm 1.0

NetWasm 1.0 does not mean “desktop .NET, but in Wasm.” It means the smaller
platform has a useful, documented boundary that applications can depend on.

The [support inventory](support-status.md) is the source of truth for what is
implemented today. Everything below is a release gate, not a current support
claim.

## In progress: .NET Standard 2.1 compatibility

This work targets ordinary NetWasm applications consuming compatible .NET
Standard 2.1 library binaries and NuGet assets without source forks or patched
third-party DLLs. Compatibility follows the reachable application: unused
unsupported functionality does not disqualify an entire library, while required
unsupported operations fail during compilation.

This is broader library compatibility within the smaller NetWasm profile, not a
claim that every .NET Standard API or package works. General runtime reflection,
`dynamic`, runtime assembly loading, runtime code generation, managed threading,
unsupported native or host APIs and named-culture globalization remain outside
the initial slice. Results attach to a package version, selected assets and
the functionality an application actually uses.

## Planned: WASI 0.3 and plain core modules

Two output changes are planned together.

**WASI 0.3.** The WASI and Component Model integration moves to WASI 0.3, whose
native async (async functions, streams and futures) lets C# `async` code map
onto the Component Model directly instead of through NetWasm's own completion
boundaries, and the set of WASI capabilities ordinary C# can use widens with it.

**Plain core modules.** Some hosts embed a WebAssembly engine without the
Component Model, such as plugin systems in games and native applications, and
use WIT only to describe the interface. For them NetWasm will also produce a
plain core module: a world's imports become core imports and its exports core
exports, named and passed with the canonical ABI as wit-bindgen's core-module
bindings do, so records, variants, enums and the other WIT types work on both
sides. Beyond the world's own imports, such a module needs only a small,
documented contract from its host.

## Release gates

| Area | The v1.0 bar |
| --- | --- |
| Practical WASI Preview 2 | Ordinary C# can use files and directories, streams and polling, CLI arguments and environment, clocks, randomness, outgoing and incoming HTTP, and TCP/UDP/DNS where the host grants those capabilities. Browser and standalone adapters publish the capability pairs they support, with consistent lifetime/error semantics and clear failure for unavailable operations. WebSockets use an explicit versioned host contract because WASI P2 has no standard WebSocket interface. Internal completion-oriented boundaries keep a later P3 migration out of application APIs; v1.0 does not wait for P3. |
| Static native interoperability | Applications can link compatible Wasm `.a` archives, use source-generated `LibraryImport` plus a bounded raw `DllImport` C ABI, and expose supported `UnmanagedCallersOnly` callbacks. Archive dependencies, relocations, memory ownership, callbacks, GC safety and failure diagnostics are deterministic and pay-as-you-use. Dynamic libraries, runtime symbol lookup, native threads and a direct C++ ABI remain outside the profile. |
| Globalization and resources | Invariant, selected-culture and full-data modes provide documented culture-aware formatting and parsing, casing, comparison, Unicode normalization and properties, calendars, resource fallback and timezone behaviour. Versioned data stays optional so an invariant application does not carry it. Ordinary applications do not need native ICU installed on the build or deployment machine. |
| Expression trees | Applications can construct and inspect `System.Linq.Expressions` trees without requiring general runtime reflection. `Expression.Compile()` has an interpreted implementation for trees created at runtime, with an optional build-time generated fast path for expressions known during compilation. Neither path depends on runtime IL generation or a JIT. |
| Compiler and runtime correctness | The supported CIL and library profile has no known correctness bugs. GC, generics, variance and casts, interface and virtual dispatch, exceptions, async continuations, static initialization, callbacks and interop are covered by retained regression and compatibility suites. Builds are deterministic, and CLI and browser pipelines produce the same bytes from the same inputs. |
| Fast development loop | Unchanged and reverted inputs reuse persistent compiler, runtime-link and component artifacts. An ordinary method edit avoids replaying unaffected whole-program work. Release builds keep full optimization and validation without making every edit pay publish-time costs. |
| Stable platform contract | NetWasm 1.0 introduces the `netwasm1.0` target profile; `netwasm0.1` remains the 0.x profile instead of changing in place. The target profile, runtime ABI, host capability contracts, package compatibility rules and diagnostics have an explicit 1.x compatibility policy. Later target-profile versions change for platform-contract breaks, not merely because a package was released. Deprecations and unsupported operations fail clearly instead of silently changing behaviour. |
| Diagnostics and production delivery | Managed stack traces, deterministic Debug output, trap/crash symbolization and actionable compiler/linker diagnostics cover supported deployments. Restore, build, test and publish work from packages on macOS ARM64, Linux ARM64/x64 and Windows ARM64/x64. Wasmtime plus Chromium, Firefox and WebKit release gates cover the portable component and browser paths. |
| Ecosystem and supply chain | The reference pack and ported libraries publish a generated member-level support catalogue. Templates, TUnit/VSTest, WIT bindings and the supported libraries work as one qualified package graph. Toolchains are pinned, release artifacts are reproducible and attributable, and capability/resource boundaries have focused hostile-input tests. |

## Explicit non-goals

The roadmap does not include runtime reflection, `dynamic`, runtime assembly
loading, managed threads or a promise that arbitrary desktop NuGet packages
work unchanged.

wasm64 core modules remain useful, but wasm64 Components cannot become a v1.0
gate until the upstream Component Model tools and hosts support them end to end.

[Back to the README](../README.md#road-to-10)
