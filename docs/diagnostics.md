# Compiler diagnostics

## Managed exceptions and diagnostic profiles

NetWasm does not promise desktop .NET exception diagnostics. Managed exception
control flow is separate from optional rich reports and stack instrumentation.
The size-first Release profile normally leaves those optional features out.

| SDK property | Debug default | Release default | Purpose |
| --- | --- | --- | --- |
| `NetWasmStructuredDiagnostics` | `true` | `false` | Structured managed failure reporting for the selected execution contract |
| `NetWasmManagedStackTrace` | `true` | `false` | Managed stack instrumentation and the frame names in `<program>.wasm.method-symbols`; also backs `System.Diagnostics.StackTrace` and `StackFrame` |
| `NetWasmTypeNames` | `true` | `false` | Every type's and assembly's names in `<program>.wasm.type-symbols`, which the program reads. Without them a name reads `type#<id>`, an assembly name `<Assembly name unavailable>`, and looking a type up by name (`Type.GetType(string)`, `Assembly.GetType(string)`, `GetNestedType(string)`, `GetInterface(string)`) throws `PlatformNotSupportedException` |

The switches are independent. Enable the desired features explicitly, for
example for an application that needs rich Release diagnostics:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <NetWasmStructuredDiagnostics>true</NetWasmStructuredDiagnostics>
  <NetWasmManagedStackTrace>true</NetWasmManagedStackTrace>
</PropertyGroup>
```

Setting either property to `false` also overrides its Debug default. These
features are pay-as-you-use and do not enable reflection or general runtime
type discovery. Do not require desktop-style messages, formatted exception
text or stack frames from a deployment whose selected profile omits them.

Supported interop boundaries can still expose a managed type ID, the type's
name from `<program>.wasm.type-symbols` and the stored message when available.
Every build writes the names of its exception types to that file, so a host's
crash report names the type even when the program itself reads `type#<id>`. Turning diagnostics off is not a
security guarantee that application messages are erased. JavaScript managed
error payloads preserve the stored nullable message, not a synthesized result
of the managed `Exception.Message` getter. Null and an explicitly empty string
remain distinct. The public JavaScript error uses the available message, then
the mapped type name, then a generic export-failure fallback. Stack details
depend on the selected instrumentation/symbols. Applications should inspect
the supported payload rather than assume desktop `Exception.ToString()` output.

A faulted `Task` whose exception nothing observes writes one line, "NetWasm: an
unobserved managed Task exception was finalized", to standard error when it is
collected. Handle `TaskScheduler.UnobservedTaskException` to receive the
exception itself.

## Program sidecars

A build writes a program's optional data beside it, each file named after the
program:

| File | Holds | Written |
| --- | --- | --- |
| `<program>.wasm.type-symbols` | Type names by runtime type ID | Every build: the exception types; with `NetWasmTypeNames`, every type |
| `<program>.wasm.method-symbols` | Stack frame names by method ID, with file and line in Debug | With `NetWasmManagedStackTrace` |
| `<program>.wasm.tz-info` | Time zone data | When local time is reachable; see [timezone assets](timezone-assets.md) |

The deployment manifest records each file. The program opens them read-only
from one guest directory, `/netwasm-sidecars/`, under the names the build gave
them: `App.wasm` opens `/netwasm-sidecars/App.wasm.type-symbols`. NetWasm's
JavaScript hosts mount them there. Under Wasmtime, mount the folder that holds
the program:

```sh
wasmtime run --dir bin/Debug/netwasm0.1::/netwasm-sidecars bin/Debug/netwasm0.1/App.wasm
```

A program built with `NetWasmTypeNames` or `NetWasmManagedStackTrace` reads its
symbols files at startup. A component without a process entry starts in the
first call to one of its exports, and the Component Model never lets a
synchronous WIT function wait, so a component that exports one (rather than an
`async func`) reads none and its names read `type#<id>`; a JavaScript module
still reads them. Each file carries the build ID of the program that
wrote it. If a file is missing, unreadable, malformed or from another build, the
program writes one line to standard error, for example
`NetWasm: skipped /netwasm-sidecars/App.wasm.type-symbols (the host mounts no /netwasm-sidecars directory); type names read as type#<id>.`,
and runs as if it was built without that file: names read `type#<id>`, looking
a type up by name throws `PlatformNotSupportedException`, and frames read
`method#<id>`. Time zone data is different: a `TZ` the program cannot
find in its data fails at startup.

## Compiler diagnostic format

The diagnostic format is `NWdddd: [Type::Method IL_000a:] message`; the
reserved `GenericExpansion` code is formatted as `NWA2001`. The complete enum
is defined in `NetWasm.Compiler.Core`.

For current product status, including the wasm64 Component Model boundary
reported by `NW1010`, see the [support status and roadmap](support-status.md).

| Code | Exact scope | Cause and action |
| --- | --- | --- |
| `NW1000` `InvalidCommandLine` | `nwc` command line, response files and export selection | Unknown/missing/repeated/malformed options, an unreadable response file, invalid target, platform or method syntax, or duplicate export. Correct the command (`Namespace.Type::Method`; platform `wasm32` or `wasm64`). |
| `NW1001` `UnsupportedCil` | CIL dispatch/lowering, exception lowering, module-data emission, and unsupported address/atomic/calli shapes | Valid CIL has no current lowering. Rewrite the reachable code or library to a supported shape; retain method/IL location when reporting an expected feature. |
| `NW1002` `InvalidCil` | Method-body decoding, token decoding, stack validation, CFG and exception-region validation | The body/token stream is malformed. Rebuild with a conforming compiler or remove the malformed method; do not treat malformed input as a feature gap. |
| `NW1003` `UnsupportedMetadata` | Metadata, interop declarations, closed-world analysis, and type/layout resolution | Readable metadata uses an unsupported signature/token/layout, open generic body, or interop shape. Compile against NetWasm.CoreLib, close generics, or adapt the API shape. |
| `NW1004` `AssemblyResolution` | Assembly/type/method/field/reference-closure resolution | A required, ambiguous, unresolved, or forbidden desktop identity was found. Pass the complete matching `--reference` closure and avoid desktop references. |
| `NW1005` `DuplicateAssembly` | Input/reference loading | Two loaded assemblies have one identity. Remove one copy. |
| `NW1006` `InvalidEntryPoint` | Entry lookup and Wasm entry validation | `--entry` is absent/non-unique or is not static `int Method(int)`; a Wasm entry cannot return a managed value type. Select a valid entry or use an explicit export/WIT export. |
| `NW1007` `IrreducibleControlFlow` | Control-flow structurization | A valid method graph cannot be represented by the current structured Wasm lowering. Rewrite the reachable control flow. |
| `NW1008` `RuntimeContract` | Layout/runtime-state and Wasm emission lookups | A compiler-generated function index, layout, exception object, or scalar memory contract is absent/inconsistent. Report compiler/runtime state with the complete command. |
| `NW1009` `ComponentContract` | WIT reading/validation, world selection, component reachability, and binding conflict checks | WIT is absent/ambiguous/async, bindings are outside the selected world, or exports conflict. Select one supported synchronous world and align declarations/options; for a binding outside the world, the message names the interface the program reaches and the `import` or `export` line that adds it. |
| `NW1010` `ComponentToolchain` | Component capability checks and external `wasm-tools`/`wasm-merge`/`wasm-opt` execution | A tool is unavailable/fails, or wasm64 Component packaging is unsupported. Install/verify pinned tools or use wasm32 for Components; wasm64 is never silently downgraded. |
| `NW1011` `CompilerInvariant` | Metadata, reachability, structured-program, layout, roots, emission, and complexity validators | Compiler state violated an internal invariant. Report it as a compiler defect, not an ordinary compatibility error. |
| `NW1012` `NativeInterop` | Static native declarations, ABI layouts, imports, callback exports, and reachability | A reachable declaration is outside the documented static C profile or conflicts with another export. Use a supported shape or isolate it from the closed world. |
| `NW1013` `FunctionLimit` | Module encoding, right after emission and before linking or optimization | A method compiles to a WebAssembly function past a limit browsers, Node and Wasmtime put on one function: a body larger than 7,654,321 bytes, more than 50,000 locals (its parameters among them) or more than 1,000 parameters. The diagnostic names the managed method and the limit | Split the method, for example a large generated initializer, into smaller methods, or pass many values in an array or an object |
| `NW1014` `HostFailure` | `nwc` reading inputs and writing outputs | The host failed outside the compiler, such as a missing input or an unwritable output directory. Correct the path or permission the message names. |
| `NW1015` `PlatformService` | Preparing compilation, before reachability | A `NetWasmPlatformServiceAttribute` binding is invalid: it names a type that is not a platform service contract, binds one contract twice, binds an implementation that is an interface, abstract, a value type, generic, lacks a public parameterless constructor or does not implement the contract, or binds `ITimerScheduler` in a program with WASI, whose timers run on the Component Model's event loop | Correct the attribute the message names |
| `NW1016` `MissingApi` | Method and field reference binding after the declaring type resolves | The selected implementation has no member matching the requested name, signature, static/instance shape and generic arity. Check that package and implementation versions agree. If they do, use a supported API or an alternative library path. Adding unrelated references does not supply a missing member on an already resolved type. |
| `NW1017` `UnlinkedNativeLibrary` | Warning, after reachability | A reached P/Invoke names a native library that no `NativeLibrary` item provides, so it compiles to a body that throws `DllNotFoundException` when called. Reference the package or `NativeLibrary` item that provides the library, or leave the call unreached. See [static native interop](static-native-interop.md#library-and-package-propagation). |
| `NWA2001` `GenericExpansion` | Identifier formatting only; no current production emitter | The enum reserves this identifier but no current source path emits it. Preserve and report the exact value if observed. |

When available, method and IL offset are part of the diagnostic. The CLI also
accepts diagnostic-log, diagnostic-trace, stack-symbol, and source-provenance
paths; these are diagnostic artifacts rather than deployment outputs.

## Package compatibility failures

A missing API (`NW1016`) differs from a missing or ambiguous binding (`NW1004`).
The former means the compiler found the declaring type but not the requested
member contract. It can indicate a version mismatch or an API that the selected
implementation does not provide; it does not by itself identify a compiler bug.
Unsupported CIL, metadata shapes and static interop use their respective codes.
An internal invariant failure (`NW1011`) should be reported as a compiler defect.

A reached type cannot inherit from a base that the selected implementation seals.
That inheritance is rejected as unsupported metadata. Direct use of the sealed
type remains valid; the restriction concerns the deriving type's required contract.

When available, errors include the originating NuGet package ID/version and
selected asset, plus a requiring path from application code through library
methods, type requirements or containing fields. The path describes the known
compile-time requirement. It is not a managed runtime stack trace. Package details
can be absent for direct DLL references, and independently rooted requirements do
not invent an application caller.

## Compiler support bundles

Use `-diagnosticlog:<path>` when only the structured `ILogger` event stream
is needed. It writes flushed JSON Lines and is disabled when the option is
absent.

For a reproducible compiler report, use `-diagnostictrace:<path>` and pass
each relevant source with `-source:<path>`. The compiler writes the primary
trace to `<path>` and progressive artifacts to `<path>.passes/`. That
directory includes the structured compiler log, normalized options,
toolchain provenance, copied source inputs, stage snapshots, emitted Wasm,
and a reproduction command. It is populated progressively, so early failures
still leave the artifacts reached before the failure.

Zip the trace file and its `.passes` directory together when reporting a
compiler defect. These opt-in artifacts can contain application source,
absolute paths, method names, compiler state, and exception details. Review
the archive before sharing it and use a private support channel when it
contains non-public code.
