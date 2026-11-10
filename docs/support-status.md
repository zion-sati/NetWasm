# Support status and roadmap

NetWasm is a pre-1.0, closed-world .NET-to-WebAssembly toolchain. This page is
the canonical public status inventory: it separates implemented contracts from
work that is only partly qualified, planned, blocked by tools, or deliberately
outside the platform profile. It does not promise a release date or imply that
an unlisted desktop API is available.

Read this with the [targets and output formats](targets-and-outputs.md),
[CoreLib/runtime compatibility](corelib-runtime.md), and [compiler
diagnostics](diagnostics.md) references. A library is compatible only when its
reachable CIL, API dependencies, target, and host capabilities fit all of those
contracts.

## How to read the statuses

| Status | Meaning |
| --- | --- |
| Complete / supported | Implemented within the stated NetWasm profile and covered by the retained target/host acceptance appropriate to that contract. This status is a feature-support statement, not a desktop-API or 1.x ABI stability guarantee. |
| Conditional | Implemented only when the selected target and host provide the documented capability or deployment input. The capability is never implicit; absence fails clearly or leaves the API outside the selected application profile. |
| Partially qualified | The implementation and focused evidence exist, but a required public qualification cell, final-link step, or distribution hand-off remains incomplete. Do not treat it as a published framework package. |
| In progress | Some groundwork or implementation exists, but NetWasm makes no public support claim for the feature. |
| Deferred | Deliberately designed or postponed work; no current feature flag or API support follows from its design. |
| External toolchain blocker | NetWasm cannot complete the requested target because required third-party Component Model tooling is not available end to end. |
| Intentionally unsupported | Outside the closed-world, single-reactor profile by design, rather than merely unfinished. |

## Complete / supported

### Core language, runtime, and selected library profile

The supported profile includes C# 15 in stable language mode when the project
selects the .NET 11 SDK. Projects using earlier language versions remain
supported with the .NET 10 SDK. NetWasm consumes the resulting CIL, so this
does not add runtime reflection, `dynamic`, runtime assembly loading, or other
features outside the profile described below.

The supported profile includes direct CIL-to-Wasm compilation, closed-world
reachability and concrete generic specialization, exact GC, exceptions,
finalization, value types, arrays, delegates, generic interface/virtual
dispatch, strings, invariant formatting, scalar numerics including `decimal`,
the bounded interpreter-only [expression-tree execution
profile](expression-trees.md), and the selected collection, stream, date/time,
task, cancellation, JavaScript interop, WIT, and WASI contracts described in
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

Supported development hosts are macOS ARM64, Linux ARM64/x64 and Windows
ARM64/x64. The SDK restores one native tool package for the development host;
Windows ARM64 requires the native ARM64 .NET SDK. An x64 .NET SDK running under
Windows ARM64 emulation cannot select the ARM64 tool package.
The pinned macOS Node binary requires macOS 13.5 or newer. Linux host tools
require glibc 2.28 or newer; the restored Linux package includes Node's
`libatomic.so.1` dependency.

| Output boundary | wasm32 | wasm64 / Memory64 |
| --- | --- | --- |
| Core module | Supported | Supported |
| Raw core module with generated JavaScript adapter | Conditional: the host supplies the explicit JavaScript contract | Conditional: the host must implement Memory64; addresses cross the adapter as checked `BigInt` values |
| WASI 0.3 Component | Conditional: the host must implement the Component Model with its async ABI and WASI 0.3, and provide every import the component declares | Not available; see [external toolchain blocker](#external-toolchain-blocker) |

Chromium is the primary browser acceptance target. Firefox is retained for
compatibility coverage. WebKit is exercised for wasm32; it is not a wasm64
Memory64 execution target. A wasm64 raw-core deployment therefore requires a
Memory64-capable host. A Component host, such as Wasmtime 50 (`wasmtime run
-Wcomponent-model-async -Sp3`, or `wasmtime serve -Sp3 -Scli` for an HTTP
service) or NetWasm's Node.js and browser hosts, must implement the imports
declared by the selected world. NetWasm never uses WASI Preview 1; an engine
without the Component Model can load a plain core module (`<OutputType>CoreModule</OutputType>`),
which has no WASI, so its system APIs throw `PlatformNotSupportedException`
unless the host provides those services through the module's world.

| Project | Output | Runs in |
| --- | --- | --- |
| Command (`<OutputType>Exe</OutputType>`, `Main`) | Component, `wasi:cli/command` | Wasmtime, wasmCloud, Node.js (the published folder), `dotnet run` |
| Component (`<OutputType>Component</OutputType>`) | Component exporting the world `NetWasmWorld` selects | Other components, Wasmtime, Spin, wasmCloud, jco |
| HTTP service (a Component whose world is `service`) | Component, `wasi:http/service` | `wasmtime serve`, Spin, wasmCloud |
| WIT worker (`<OutputType>WitWebWorker</OutputType>`) | Component with the application's world | Browser workers |
| JavaScript module (`<OutputType>JsModule</OutputType>`) | Raw core module and NetWasm's JavaScript host | Browser pages and workers, Node.js |
| Plain core module (`<OutputType>CoreModule</OutputType>`) | Self-contained core module importing only its world, no WASI | Any engine with the required features |

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

The retained date/time profile provides Gregorian arithmetic, invariant
formatting and parsing, ordinary non-leap file-time conversion, and selected
IANA-zone conversion. Named cultures, alternate calendars, `IConvertible`,
user-created `TimeZoneInfo`, legacy timezone string serialization, and automatic
IANA/Windows identifier mapping are outside the profile. Local `DateTime`
binary serialization is rejected because its desktop payload embeds machine
timezone assumptions. `TimeZoneInfo.ClearCachedData()` clears facade object
caches but deliberately preserves the immutable TZ/asset snapshot selected at
startup; changing `TZ` during one component lifetime is unsupported.

Component capabilities are Conditional too. A component receives only the
WIT/WASI imports declared by its selected world, and its host must supply each
one before managed code begins. Clocks, streams, randomness, filesystem access,
and timezone assets are never implicitly available.

### WASI-backed .NET surface

The checked-in WASI 0.3.2 WIT closure describes upstream interfaces, and the
Hosting provider catalog describes interfaces that a host adapter can supply.
Neither is a claim that NetWasm exposes every corresponding .NET API. The
closed-world compiler retains only imports reached by the application, so a
component imports only what its program uses. Every system service goes through
the program's platform (`NetWasmPlatform`; `WasiPlatform` by default in a
component), which an application can replace with its own.

| Area | Available | Not available |
| --- | --- | --- |
| Files and directories | `System.IO.File`, `FileStream`, `Directory`, `DirectoryInfo`, `FileInfo`, `Path` and their readers and writers over WASI 0.3's filesystem, within the directories the host grants (`dotnet run` grants the working directory and a private temporary directory; a published command takes `--dir` in the Node.js launcher or Wasmtime). A browser page or worker mounts files with NetWasm's mount API. | Permissions, ownership, locking, watching, and paths outside the granted directories. |
| Networking | `System.Net.Sockets` (`Socket`, `TcpClient`, `TcpListener`, `UdpClient`, `NetworkStream`) and `Dns` over WASI 0.3's sockets, blocking and asynchronous; `ws://` WebSockets (`ClientWebSocket`, `WebSocket.CreateFromStream`); `HttpClient` (`NetWasm.System.Net.Http`) over `wasi:http`, and HTTP services that answer it. | Non-blocking sockets and socket timeouts, `Poll`/`Select`, Unix domain sockets, multicast and broadcast, binding a dual-mode socket to the IPv6 wildcard, reverse DNS, `wss://` (no TLS yet), and WebSocket compression. |
| CLI and processes | Command-line arguments, environment variables, standard input, output and error, the exit code (`Environment.ExitCode` or `Main`'s result), `Environment.Exit` and `Environment.FailFast`. | `System.Diagnostics.Process`, child-process creation, shell execution, process enumeration and ambient machine/user/process discovery. |
| Concurrency | Single-reactor `Task`, `ValueTask`, cancellation, timers and readiness-driven continuations; synchronous I/O APIs over WASI's asynchronous operations inside a command or an async export. | Managed threads, workers sharing the managed heap, a managed thread pool, `Task.Run` parallelism, blocking waits on a task, and shared-memory/atomic threading semantics. Source-compatibility shims do not provide concurrency. |
| Host and browser services | Explicit application WIT imports and explicit JavaScript imports in the applicable output mode. | Implicit DOM, fetch, storage, registry, proxy, certificate-store or other ambient OS/browser services. |

The timer entry in this profile means `TimeProvider`, `ITimer`,
`PeriodicTimer`, `System.Threading.Timer`, cancellation deadlines and
framework-owned delay/wait timers. `TaskFactory` is absent; use the retained
direct `Task` APIs and reactor continuations.

This table describes the public managed compatibility boundary. It does not
forbid Hosting from using a capability internally, nor does it turn every
interface in a selected WIT world into a .NET API.

### Managed stack traces

Rich exception diagnostics are a build-profile choice, not a promise of desktop
.NET diagnostic behavior. See [the exception-profile defaults, opt-ins and
interop payload boundaries](diagnostics.md#managed-exceptions-and-diagnostic-profiles).

SDK Debug builds enable managed stack instrumentation and its deterministic
symbol sidecar by default; Release builds leave it out unless
`NetWasmManagedStackTrace=true`. `nwc` enables the same behavior
with `-stacktracesymbols:<path>`. `Exception.StackTrace` then reports the
captured managed frames without adding general reflection or type-name metadata
to the Wasm image. This is diagnostic stack-trace support, not source-level
debugging or DWARF; see
[deferred debug information](#deferred-debug-information-dwarf).

### Type names

`Type.Name`, `Type.Namespace`, `Type.FullName` and `Type.ToString()` work on
any `Type` value, including one from `GetType()`. Looking a type up by its name,
as `Type.GetType(string)` does, is outside the profile.

The names come from data the compiler generates, and they are not free. The
first reachable read of one of these members, on any type, adds that form of
name for every type in the program, together with the runtime type facts the
names rely on: tens of kilobytes in a small application. Code that must stay
small, such as an error path every application reaches, should use a string it
already has, for example a constant or `nameof`.

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

The SDK, runtime packs and templates are released for the current `netwasm0.1`
profile. The separate library and test packages below are also published.

| Package profile | Supported boundary |
| --- | --- |
| Reflection-free dependency injection | `NetWasm.Microsoft.Extensions.DependencyInjection` and its Abstractions package provide generated activation, keyed/sequence services, scopes and package-owned open-generic closure. Runtime reflection, dynamic activation and runtime generic construction remain outside the profile. |
| `System.Xml` | `NetWasm.System.Xml` provides the selected reflection-free readers/writers, DOM, XPath, LINQ to XML, interpreted transforms and primitive serialization. Its `System.Xml.Schema` types are a minimal object model that checks only the document element's name against the schema's top-level elements; they do not perform XSD validation. A port of the .NET schema compiler and validator is in progress. Nonprimitive runtime serializer discovery and ambient network/filesystem resolution remain excluded. |
| WASI-only HTTP (`System.Net.Http`) | `NetWasm.System.Net.Http` provides upstream's `HttpClient` over a `wasi:http` 0.3 client transport for wasm32 components and NetWasm's Node.js and browser hosts, with no framework `[JSImport]`, and binds an `<OutputType>Component</OutputType>` HTTP service's `Handle(HttpRequestMessage)` to `wasi:http/handler`. wasm64 Component packaging remains externally blocked. |
| TUnit | The `NetWasm.TUnit.*` packages provide the generated catalog, sequential runner and `dotnet test` integration. A later upstream contribution is a separate decision. |

### Compiler and SDK capability acceptance

| Area | Supported boundary |
| --- | --- |
| Bounded custom attribute queries | Type-targeted queries, `DescriptionAttribute`, and user-defined attributes, including closed generics, are available on raw wasm32/wasm64 and component wasm32. Targets and filters must be statically bounded; there is no runtime metadata database or preserve-everything fallback. See the [query profile](custom-attributes.md). |
| Static native C interop | Ordinary `LibraryImport` and bounded raw `DllImport` declarations link declared Wasm `.a` providers. The supported profile covers scalar/pointer/byref calls, by-value structs/unions, counted arrays and input UTF-8, `SafeHandle` inputs, and static scalar/pointer `UnmanagedCallersOnly` callbacks across ordinary app, library, and test consumers on wasm32 and raw wasm64. Dynamic loading, broad runtime/custom marshalling, `SafeHandle` construction, arbitrary unmanaged `calli`, delegate conversion, native threads, and direct C++ ABI remain excluded. See the [static native profile](corelib-runtime.md#static-native-interop). |

## In progress

The entries in this section describe the current direction only. They are not
available application dependencies or promises of a future API shape.

| Area | Current boundary |
| --- | --- |
| Portable diagnostics | DP01–DP06 are implemented and locally qualified for in-process `DiagnosticSource`, `Activity`, propagation and Metrics under the single-reactor contract. `Debug` exposes the ten documented assertion, failure and `WriteLine` overloads: retained calls are conditional on `DEBUG`, failures throw catchable `InvalidOperationException`, and output uses standard error. Debugger attributes retain ordinary metadata and string-valued contracts, but resolving a `Type` to an assembly-qualified debugger name fails with `PlatformNotSupportedException`. Public package/reference-pack distribution remains incomplete. OS tracing, attached-debugger/provider behavior, background exporters, process/runtime metrics and reflection payload inspection are excluded. |
| `System.Uri` | U01/U02 are implemented and locally qualified for the selected portable parsing, resolution, escaping, builder and IDN/IRI profile. Public reference-pack distribution remains incomplete. |
| Credentials | `NetworkCredential` retains string-based username, password and domain values, with null values normalized to empty strings. `ICredentials` and `ICredentialsByHost` lookup return the same credential object without validating or contacting the endpoint. `SecureString` constructors, `SecurePassword` and secure-string marshaling are outside this profile. |

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

The deferred debugger direction includes Rider and Visual Studio breakpoints,
stepping, call stacks, Locals and a defined read-only Watch subset. The design
retains portable PDBs as development inputs; emitted Wasm address/storage mappings and a
debugger backend with IDE integration are also required. PDB retention or the
exception-trace sidecar alone does not enable interactive debugging. Source-aware
exception reporting is an independent immediate DX priority.

## Non-collecting Bump allocator

NetWasm supports an opt-in `Bump` production collector for applications whose
lifetime and total allocation are bounded by design. Allocation advances
monotonically through lazily allocated chunks and does not reclaim managed
objects during execution. It is suitable for one-shot tools, plugins that
finish and unload, and process-per-job workers with a known memory bound. It is
also useful as a no-collection control when diagnosing collector-sensitive
behavior, but diagnostics are not its only intended use.

Select it with `NetWasmGarbageCollector=Bump`. Memory grows until the application
ends or the Wasm memory limit is reached. The SDK rejects applications that use
finalization, weak handles or ephemeron handles with Bump. Compact remains the
default precise tracing collector, and NetWasmGC the throughput-oriented one.

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
browser tooling. NetWasm's Component contract is WASI 0.3, which does not make
wasm64 Component output available.

## Intentionally unsupported

The following are outside the closed-world, single-reactor platform profile:

- broad runtime reflection, reflection-driven serialization or activation,
  runtime code generation, `dynamic`, runtime assembly loading, and
  runtime-created generic types; expression nodes outside the bounded
  [managed-interpreter profile](expression-trees.md) remain unsupported;
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
