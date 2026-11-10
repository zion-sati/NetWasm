# Roadmap to NetWasm 1.0

NetWasm 1.0 does not mean “desktop .NET, but in Wasm.” It means the smaller
platform has a useful, documented boundary that applications can depend on.

The [support inventory](support-status.md) is the source of truth for what is
implemented today. Everything below is a release gate, not a current support
claim, unless it is marked done.

## In progress: .NET Standard 2.1 compatibility

This work targets ordinary NetWasm applications consuming compatible .NET
Standard 2.1 library binaries and NuGet assets without source forks or patched
third-party DLLs. Every .NET Standard API binds: one NetWasm cannot support
throws `PlatformNotSupportedException` when it is called, as .NET does on a
platform without the capability, so an unsupported member a library never
calls does not stop it from working.

The framework is split as .NET's is: CoreLib keeps the scope of .NET's
`System.Private.CoreLib`, and the other framework assemblies, with the
`netstandard` facade, ship as the `NetWasm.App` framework, whose contents
follow `Microsoft.NETCore.App`'s (`System.Text.Json` included).
`Microsoft.Extensions.*` and other out-of-band libraries stay NuGet packages.

This is broader library compatibility within the smaller NetWasm profile, not a
claim that every .NET Standard API or package works. General runtime reflection,
`dynamic`, runtime assembly loading, runtime code generation, managed threading,
unsupported native or host APIs and named-culture globalization remain outside
the initial slice. Results attach to a package version, selected assets and
the functionality an application actually uses.

## ✅ Done in NetWasm 0.12: WASI 0.3 and plain core modules

**WASI 0.3.** The WASI and Component Model integration is WASI 0.3, whose
native async (async functions, streams and futures) lets C# `async` code map
onto the Component Model directly instead of through NetWasm's own completion
boundaries. The set of WASI capabilities ordinary C# can use widens with it:
files and directories, sockets and DNS, `ws://` WebSockets, `HttpClient`, and
HTTP services written as `Task<HttpResponseMessage> Handle(HttpRequestMessage)`.
WASI 0.3 is stable and current Wasmtime enables it by default, so components
target WASI 0.3 only, with no Preview 2 output alongside. Applications whose
host runs only Preview 2 can stay on NetWasm 0.11.

**WASI needs the Component Model.** NetWasm's WASI support runs on engines that
implement the Component Model and WASI 0.3, such as Wasmtime, or through
NetWasm's own hosts for Node.js and browsers. NetWasm never uses WASI Preview 1.
Preview 1 is frozen, and current WASI is defined on the Component Model, so an
engine that runs current WASI programs implements it; for an engine that
supports only core WebAssembly and Preview 1, adding that support is the
engine vendor's job.

**Plain core modules.** Some hosts embed a WebAssembly engine without the
Component Model, such as plugin systems in games and native applications, and
use WIT only to describe the interface. For them NetWasm also produces a plain
core module (`<OutputType>CoreModule</OutputType>`): a world's imports become core imports and its exports core
exports, named and passed with the canonical ABI as wit-bindgen's core-module
bindings do, so records, variants, enums and the other WIT types work on both
sides. Such a module imports nothing but its world's functions, and it has no
WASI: .NET APIs that need a system service (the console, clocks and timers,
random numbers, the environment, files, networking) throw
`PlatformNotSupportedException`. A host that wants to offer one of those
services provides it through the world.

## Release gates

| Area | The v1.0 bar |
| --- | --- |
| Practical WASI 0.3 | ✅ **Done in NetWasm 0.12.0.** Components target WASI 0.3, and the Component Model's async functions, streams and futures are NetWasm's one async model. Ordinary C# can use files and directories, streams, CLI arguments and environment, clocks, randomness, outgoing and incoming HTTP, and TCP/UDP/DNS where the host grants those capabilities. Synchronous .NET APIs work on hosts that can suspend and fail clearly on hosts that cannot, such as browsers. Browser and standalone hosts publish the capabilities they support, with consistent lifetime and error semantics and clear failure for unavailable operations. `ws://` WebSockets run over WASI sockets, since WASI has no WebSocket interface; `wss://` waits for a standard WASI TLS interface. |
| Static native interoperability | **Partial support.** Applications link compatible Wasm `.a` archives, use source-generated `LibraryImport` plus a bounded raw `DllImport` C ABI, and expose supported `UnmanagedCallersOnly` callbacks; archive dependencies, relocations, memory ownership, callbacks, GC safety and failure diagnostics are deterministic and pay-as-you-use. Still to come: `SafeHandle` as a `ref`, `out` or return value, converting a delegate to a function pointer, callbacks that take structs, arrays or by-reference values, and `SetLastError`. Dynamic libraries, runtime symbol lookup, native threads and a direct C++ ABI remain outside the profile. |
| Globalization and resources | Invariant, selected-culture and full-data modes provide documented culture-aware formatting and parsing, casing, comparison, Unicode normalization and properties, calendars, resource fallback and timezone behaviour. Versioned data stays optional so an invariant application does not carry it. Ordinary applications do not need native ICU installed on the build or deployment machine. |
| Expression trees | Applications can construct and inspect `System.Linq.Expressions` trees without general runtime reflection, and `Expression.Compile()` executes the node shapes .NET's own interpreter supports through NetWasm's closed-world interpreter: the arithmetic, comparison, logical, bitwise and checked operators and `decimal`; lambdas of any arity and void delegates; construction (`New`, `MemberInit`, `ListInit`, `NewArray`); nested lambdas and `Invoke`; control flow (loops, `switch`, `try`/`catch`, `goto`); and value-type receivers. Expressions are always interpreted: NetWasm has no runtime IL generation or JIT, so there is no compiled path. |
| Compiler and runtime correctness | The supported CIL and library profile has no known correctness bugs. GC, generics, variance and casts, interface and virtual dispatch, exceptions, async continuations, static initialization, callbacks and interop are covered by retained regression and compatibility suites. Builds are deterministic, and CLI and browser pipelines produce the same bytes from the same inputs. |
| Fast development loop | ✅ **Done in NetWasm 0.11.0.** Unchanged and reverted inputs reuse persistent compiler, runtime-link and component artifacts. An ordinary method edit avoids replaying unaffected whole-program work. Release builds keep full optimization and validation without making every edit pay publish-time costs. |
| Stable platform contract | NetWasm 1.0 introduces the `netwasm1.0` target profile; `netwasm0.1` remains the 0.x profile instead of changing in place. The target profile, runtime ABI, host capability contracts, package compatibility rules and diagnostics have an explicit 1.x compatibility policy. Later target-profile versions change for platform-contract breaks, not merely because a package was released. Deprecations and unsupported operations fail clearly instead of silently changing behaviour. |
| Diagnostics and production delivery | Managed stack traces, deterministic Debug output, trap/crash symbolization and actionable compiler/linker diagnostics cover supported deployments. Restore, build, test and publish work from packages on macOS ARM64, Linux ARM64/x64 and Windows ARM64/x64. Wasmtime, Spin and wasmCloud plus Chromium, Firefox and WebKit release gates cover the portable component and browser paths. |
| Ecosystem and supply chain | The reference pack and ported libraries publish a generated member-level support catalogue. Templates, TUnit/VSTest, WIT bindings and the supported libraries work as one qualified package graph. Toolchains are pinned, release artifacts are reproducible and attributable, and capability/resource boundaries have focused hostile-input tests. |

## Explicit non-goals

The roadmap does not include runtime reflection, `dynamic`, runtime assembly
loading, managed threads or a promise that arbitrary desktop NuGet packages
work unchanged.

wasm64 core modules remain useful, but wasm64 Components cannot become a v1.0
gate until the upstream Component Model tools and hosts support them end to end.

[Back to the README](../README.md#road-to-10)
