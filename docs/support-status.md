# Support status and roadmap

NetWasm is an experimental, closed-world .NET-to-WebAssembly toolchain. This
page is the canonical public status inventory: it separates implemented
contracts from work that is only partly qualified, planned, blocked by tools,
or deliberately outside the platform profile. It does not promise a release
date or imply that an unlisted desktop API is available.

Read this with the [targets and output formats](targets-and-outputs.md),
[CoreLib/runtime compatibility](corelib-runtime.md), and [compiler
diagnostics](diagnostics.md) references. A library is compatible only when its
reachable CIL, API dependencies, target, and host capabilities fit all of those
contracts.

## How to read the statuses

| Status | Meaning |
| --- | --- |
| Complete / supported | Implemented within the stated NetWasm profile and covered by the retained target/host acceptance appropriate to that contract. It is still experimental, not a desktop-API or ABI stability guarantee. |
| Conditional | Implemented only when the selected target and host provide the documented capability or deployment input. The capability is never implicit; absence fails clearly or leaves the API outside the selected application profile. |
| Partially qualified | The implementation and focused evidence exist, but a required public qualification cell, final-link step, or distribution hand-off remains incomplete. Do not treat it as a published framework package. |
| In progress | Some groundwork or implementation exists, but NetWasm makes no public support claim for the feature. |
| Deferred | Deliberately designed or postponed work; no current feature flag or API support follows from its design. |
| External toolchain blocker | NetWasm cannot complete the requested target because required third-party Component Model tooling is not available end to end. |
| Intentionally unsupported | Outside the closed-world, single-reactor profile by design, rather than merely unfinished. |

## Complete / supported

### Core language, runtime, and selected library profile

The supported profile includes direct CIL-to-Wasm compilation, closed-world
reachability and concrete generic specialization, exact GC, exceptions,
finalization, value types, arrays, delegates, generic interface/virtual
dispatch, strings, invariant formatting, scalar numerics including `decimal`,
and the selected collection, stream, date/time, task, cancellation, JavaScript
interop, WIT, and WASI contracts described in
[CoreLib/runtime compatibility](corelib-runtime.md).

Selected adjacent-library work is also implemented within that same profile,
including synchronous and asynchronous LINQ, selected immutable/frozen and
ordered collections, regular expressions, and reflection-free,
source-generated `System.Text.Json`. These are selected contracts, not a claim
that every member in the corresponding desktop namespace is present. There is
not yet a generated member-by-member compatibility catalogue.

### Target and host boundary

The target width and host contract are separate choices. “Supported” below
means the named output boundary; it does not give a component capability to a
raw core module or make browser APIs implicit.

Supported development hosts for the first preview are macOS ARM64, Linux ARM64,
Linux x64 and Windows x64. Windows ARM64 is unsupported because Emscripten 6.0.7
does not ship an upstream native Windows ARM64 toolchain; x64 emulation is not a
supported NetWasm development-host configuration.

| Output boundary | wasm32 | wasm64 / Memory64 |
| --- | --- | --- |
| Core module | Supported | Supported |
| Raw core module with generated JavaScript adapter | Conditional: the host supplies the explicit JavaScript contract | Conditional: the host must implement Memory64; addresses cross the adapter as checked `BigInt` values |
| WASI Preview 2 Component | Conditional: the `cm32p2` toolchain and host must provide every declared WIT/WASI capability | Not available; see [external toolchain blocker](#external-toolchain-blocker) |

Chromium is the primary browser acceptance target. Firefox is retained for
compatibility coverage. WebKit is exercised for wasm32; it is not a wasm64
Memory64 execution target. A wasm64 raw-core deployment therefore requires a
Memory64-capable host. A Component host, such as Wasmtime or the pinned browser
component path, must implement the imports declared by the selected world.

## Conditional capabilities and deployment inputs

The selected `Random` and deterministic `Guid` APIs are complete/supported.
`Guid` creation, parsing, formatting, equality, hashing, and byte conversion
need no host service. `Guid.NewGuid()` is Conditional: it requires the declared
WASI random capability. There is no weak pseudo-random or JavaScript fallback
when that capability is absent.

UTC `System.TimeZoneInfo` behaviour is complete/supported. Local
`System.TimeZoneInfo` and timezone-asset behaviour are Conditional: the host
must provide a deployment-selected, versioned asset through standard WASI
environment (`TZ`) and filesystem capabilities. Absent, corrupt, incompatible,
or incomplete assets fail loudly instead of falling back to host-local time;
there is no custom timezone WIT import. See [timezone assets](timezone-assets.md).

Component capabilities are Conditional too. A component receives only the
WIT/WASI imports declared by its selected world, and its host must supply each
one before managed code begins. Clocks, streams, randomness, filesystem access,
and timezone assets are never implicitly available.

### First-preview WASI-backed .NET surface

The checked-in WASI 0.2.11 WIT closure describes upstream interfaces, and the
Hosting provider catalog describes interfaces that a host adapter can supply.
Neither is a claim that NetWasm exposes every corresponding .NET API. The
closed-world compiler retains only imports reached by the application, and the
current managed surface is deliberately narrower:

| Area | Available in the first preview | Not implemented as a public managed surface |
| --- | --- | --- |
| Files and directories | Abstract and in-memory streams, readers/writers and pipelines. Local-time support may read one fixed, read-only timezone sidecar through an explicitly mounted preopen. | General path-backed `System.IO.File`, `FileStream`, `Directory`, directory enumeration, mutation, watching and arbitrary preopen access. Public file APIs are deferred until after the WASI 0.3 migration. |
| Networking | The selected `System.Net.Http` client uses declared WASI HTTP capabilities in its qualified profile. | `System.Net.Sockets`, raw TCP/UDP sockets, listeners and general DNS APIs. The presence of socket WIT definitions or a host provider does not make these managed APIs available. |
| CLI and processes | Selected command-line arguments, environment access, standard output/runtime diagnostics and application exit plumbing. | `System.Diagnostics.Process`, child-process creation, shell execution, process enumeration and ambient machine/user/process discovery. |
| Concurrency | Single-reactor `Task`, `ValueTask`, cancellation, timers and readiness-driven continuations. | Managed threads, workers sharing the managed heap, a managed thread pool, `Task.Run` parallelism, blocking waits and shared-memory/atomic threading semantics. Source-compatibility shims do not provide concurrency. |
| Host and browser services | Explicit application WIT imports and explicit JavaScript imports in the applicable output mode. | Implicit DOM, fetch, storage, registry, proxy, certificate-store or other ambient OS/browser services. |

This table describes the public managed compatibility boundary. It does not
forbid Hosting from using a capability internally, nor does it turn every
interface in a selected WIT world into a .NET API.

### Managed stack traces

SDK Debug builds enable managed stack instrumentation and its deterministic
symbol sidecar by default; Release builds leave it out unless
`NetWasmManagedStackTrace=true`. The compiler CLI enables the same behavior
with `--stack-trace-symbols <path>`. `Exception.StackTrace` then reports the
captured managed frames without adding general reflection or type-name metadata
to the Wasm image. This is diagnostic stack-trace support, not source-level
debugging or DWARF; see
[deferred debug information](#deferred-debug-information-dwarf).

## Complete / supported package profiles

### `System.IO.Hashing` scalar profile

The reflection-free scalar `System.IO.Hashing` profile is implemented for
`Adler32`, `Crc32`, `Crc64`, their parameter sets, `XxHash32`, `XxHash64`,
`XxHash3`, `XxHash128`, and the selected synchronous/asynchronous stream forms.
It deliberately contains no Wasm SIMD, hardware intrinsic, browser JavaScript,
or host-hashing fallback.

The selected scalar profile is qualified and published as
`NetWasm.System.IO.Hashing`. wasm64 Component packaging remains an
[external toolchain blocker](#external-toolchain-blocker), not an unpublished
library profile.

### SDK/templates package acceptance

The immutable `NetWasm.Ref`, SDK, runtime-pack, and template package graph
passes the package-only developer-loop gate for `dotnet new`, restore,
dual-target library builds, `dotnet build`, `dotnet run`, portable publish,
pinned `jco` browser translation, and clean. The generic VSTest bridge and
TUnit package also pass `dotnet test -f netwasm0.1` integration.

The SDK, runtime packs and templates are released for the experimental
`netwasm0.1` profile. The separate library and test packages below are also
published.

| Package profile | Supported boundary |
| --- | --- |
| Reflection-free dependency injection | `NetWasm.Microsoft.Extensions.DependencyInjection` and its Abstractions package provide generated activation, keyed/sequence services, scopes and package-owned open-generic closure. Runtime reflection, dynamic activation and runtime generic construction remain outside the profile. |
| `System.Xml` | `NetWasm.System.Xml` provides the selected reflection-free readers/writers, DOM, XPath, LINQ to XML, schemas, interpreted transforms and primitive serialization. Nonprimitive runtime serializer discovery and ambient network/filesystem resolution remain excluded. |
| WASI-only HTTP (`System.Net.Http`) | `NetWasm.System.Net.Http` provides the qualified WASI HTTP Preview 2 wasm32 native/browser client profile, with a version-neutral transport seam and no framework `[JSImport]`. wasm64 Component packaging remains externally blocked. |
| TUnit | The `NetWasm.TUnit.*` packages provide the generated catalog, sequential runner and `dotnet test` integration. A later upstream contribution is a separate decision. |

## In progress

The entries in this section describe the current direction only. They are not
available application dependencies or promises of a future API shape.

| Area | Current boundary |
| --- | --- |
| Portable diagnostics | DP01–DP06 are implemented and locally qualified for in-process `DiagnosticSource`, `Activity`, propagation and Metrics under the single-reactor contract. Public package/reference-pack distribution remains incomplete. OS tracing, background exporters, process/runtime metrics and reflection payload inspection are excluded. |
| `System.Uri` | U01/U02 are implemented and locally qualified for the selected portable parsing, resolution, escaping, builder and IDN/IRI profile. Public reference-pack distribution remains incomplete. |

## Deferred SIMD and Packed SIMD target-option design

Wasm SIMD is not enabled today. The existing portable `Vector<T>` path is
scalar, and `Vector<T>.IsHardwareAccelerated` remains false for the current
target model. There is no current `simd` compiler option, `wasm128` artifact,
or supported `Vector128<T>` or
`System.Runtime.Intrinsics.Wasm.PackedSimd` surface.

The deferred design uses a compile-time target capability (`none` or `wasm128`)
that participates in artifact identity, manifests, caches, validation, and
deployment selection. A `none` artifact must contain no SIMD instructions; a
`wasm128` artifact may lower selected operations directly. Capability checks
must be specialized before reachability so guarded SIMD code is removed from a
baseline artifact. Automatic vectorization is a separate, later optimization
and is never required for correctness.

x86 and ARM intrinsics, `Vector64<T>`, `Vector256<T>`, and `Vector512<T>` are
intentionally outside this planned SIMD profile.

## Deferred debug information (DWARF)

DWARF is not emitted today. The only planned DWARF mode is deterministic,
unoptimized Debug output with truthful source line/function data for wasm32 and
wasm64. Optimized or Release DWARF, reconstruction of optimized-away values,
and a partial source-debugging experience are intentionally unsupported. The
managed stack-trace sidecar is separate and remains the available diagnostic
facility.

## Deferred non-collecting allocator mode

NetWasm may add an opt-in bump allocator (a non-collecting or "null GC"
runtime mode) for short-lived applications and diagnostics. Allocation would
advance monotonically through the managed heap and would not reclaim objects
during execution. This can remove collector and root scanning from a failing
program while preserving the same compiled managed workload, making it a
useful control when diagnosing GC-lifetime defects.

This mode is deferred. Precise BDWGC remains the supported managed-heap
implementation, and a non-collecting mode must not mask defects in that
collector. Any future implementation must be explicitly selected, document
its unbounded-until-limit memory behavior and reduced collection/finalization
semantics, and preserve the runtime allocation ABI so application code does
not depend on a particular collector.

## External toolchain blocker

### wasm64 Components, `cm64p2`, and `NW1010`

NetWasm can compile and run wasm64 core modules through the raw-core host
boundary. It cannot currently produce a wasm64 WebAssembly Component. The
required external Memory64 Component path—commonly identified by the `cm64p2`
canonical target name—does not yet provide the complete encoder, validator,
runtime, and browser-tooling support required to package and execute the
Component.

For that request, the compiler reports [`NW1010`](diagnostics.md#compiler-diagnostics)
and produces no component. It never silently changes the request to wasm32.
Use wasm32 for the current Component flow, or wasm64 as an explicit raw core
module with its own host contract.

WASI 0.3 does not remove this blocker. A wasm32 WASI 0.3 transport evolution is
independent of `cm64p2`: it neither solves nor waits on the missing Memory64
Component encoder, Canonical ABI support, runtime support, validation, and
browser tooling. NetWasm's current Component contract is WASI 0.2; qualifying a
wasm32 transport evolution would not make wasm64 Component output available.

## Intentionally unsupported

The following are outside the closed-world, single-reactor platform profile:

- broad runtime reflection, reflection-driven serialization or activation,
  expression-tree compilation, `dynamic`, runtime assembly loading, and
  runtime-created generic types;
- managed threads, workers sharing a managed heap, a managed thread pool,
  `Task.Run`-style parallel work, blocking waits, and cross-thread memory-order
  guarantees;
- concurrent collections and desktop synchronization/atomic APIs that require
  a multi-threaded runtime profile;
- implicit DOM, fetch, storage, or other browser capability access. Use explicit
  application JavaScript imports for browser-specific services, WIT for a
  declared portable service, or WASI for a standardized capability;
- ambient desktop filesystem, process, registry, sockets, proxy, certificate,
  and OS-discovery behaviour. A selected capability may be added only through a
  documented WASI/WIT contract; and
- implicit culture/ICU data, culture-sensitive collation, and host timezone
  databases. The base text profile is ordinal and timezone data is an explicit
  deployment asset.

Some compatibility declarations may compile but throw
`PlatformNotSupportedException` or `NotSupportedException` when their behavior
is outside this profile. The [CoreLib/runtime compatibility](corelib-runtime.md)
page records the governing boundary, and [compiler diagnostics](diagnostics.md)
records every current NetWasm-specific diagnostic.
