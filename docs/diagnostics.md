# Compiler diagnostics

## Managed exceptions and diagnostic profiles

NetWasm does not promise desktop .NET exception diagnostics. Managed exception
control flow is separate from optional rich reports and stack instrumentation.
The size-first Release profile normally leaves those optional features out.

| SDK property | Debug default | Release default | Purpose |
| --- | --- | --- | --- |
| `NetWasmStructuredDiagnostics` | `true` | `false` | Structured managed failure reporting for the selected execution contract |
| `NetWasmManagedStackTrace` | `true` | `false` | Managed stack instrumentation and its symbol sidecar |

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
type-name discovery. Do not require desktop-style messages, formatted exception
text or stack frames from a deployment whose selected profile omits them.

Supported interop boundaries can still expose a managed type ID, sidecar-mapped
type name and stored message when available. Turning diagnostics off is not a
security guarantee that application messages are erased. JavaScript managed
error payloads preserve the stored nullable message, not a synthesized result
of the managed `Exception.Message` getter. Null and an explicitly empty string
remain distinct. The public JavaScript error uses the available message, then
the mapped type name, then a generic export-failure fallback. Stack details
depend on the selected instrumentation/symbols. Applications should inspect
the supported payload rather than assume desktop `Exception.ToString()` output.

## Compiler diagnostic format

The diagnostic format is `NWdddd: [Type::Method IL_000a:] message`; the
reserved `GenericExpansion` code is formatted as `NWA2001`. The complete enum
is defined in `NetWasm.Compiler.Core`.

For current product status, including the wasm64 Component Model boundary
reported by `NW1010`, see the [support status and roadmap](support-status.md).

| Code | Exact scope | Cause and action |
| --- | --- | --- |
| `NW1000` `InvalidCommandLine` | CLI parsing and export selection | Unknown/missing/repeated/malformed options, invalid target or method syntax, or duplicate export. Correct the command (`Namespace.Type::Method`; target `wasm32` or `wasm64`). |
| `NW1001` `UnsupportedCil` | CIL dispatch/lowering, exception lowering, module-data emission, and unsupported address/atomic/calli shapes | Valid CIL has no current lowering. Rewrite the reachable code or library to a supported shape; retain method/IL location when reporting an expected feature. |
| `NW1002` `InvalidCil` | Method-body decoding, token decoding, stack validation, CFG and exception-region validation | The body/token stream is malformed. Rebuild with a conforming compiler or remove the malformed method; do not treat malformed input as a feature gap. |
| `NW1003` `UnsupportedMetadata` | Metadata, interop declarations, closed-world analysis, and type/layout resolution | Readable metadata uses an unsupported signature/token/layout, open generic body, or interop shape. Compile against NetWasm.CoreLib, close generics, or adapt the API shape. |
| `NW1004` `AssemblyResolution` | Assembly/type/method/field/reference-closure resolution | A required, ambiguous, unresolved, or forbidden desktop identity was found. Pass the complete matching `--reference` closure and avoid desktop references. |
| `NW1005` `DuplicateAssembly` | Input/reference loading | Two loaded assemblies have one identity. Remove one copy. |
| `NW1006` `InvalidEntryPoint` | Entry lookup and Wasm entry validation | `--entry` is absent/non-unique or is not static `int Method(int)`; a Wasm entry cannot return a managed value type. Select a valid entry or use an explicit export/WIT export. |
| `NW1007` `IrreducibleControlFlow` | Control-flow structurization | A valid method graph cannot be represented by the current structured Wasm lowering. Rewrite the reachable control flow. |
| `NW1008` `RuntimeContract` | Layout/runtime-state and Wasm emission lookups | A compiler-generated function index, layout, exception object, or scalar memory contract is absent/inconsistent. Report compiler/runtime state with the complete command. |
| `NW1009` `ComponentContract` | WIT reading/validation, world selection, component reachability, and binding conflict checks | WIT is absent/ambiguous/async, bindings are outside the selected world, or exports conflict. Select one supported synchronous world and align declarations/options. |
| `NW1010` `ComponentToolchain` | Component capability checks and external `wasm-tools`/`wasm-merge`/`wasm-opt` execution | A tool is unavailable/fails, or wasm64 Component packaging is unsupported. Install/verify pinned tools or use wasm32 for Components; wasm64 is never silently downgraded. |
| `NW1011` `CompilerInvariant` | Metadata, reachability, structured-program, layout, roots, emission, and complexity validators | Compiler state violated an internal invariant. Report it as a compiler defect, not an ordinary compatibility error. |
| `NW1012` `NativeInterop` | Static native declarations, ABI layouts, imports, callback exports, and reachability | A reachable declaration is outside the documented static C profile or conflicts with another export. Use a supported shape or isolate it from the closed world. |
| `NWA2001` `GenericExpansion` | Identifier formatting only; no current production emitter | The enum reserves this identifier but no current source path emits it. Preserve and report the exact value if observed. |

When available, method and IL offset are part of the diagnostic. The CLI also
accepts diagnostic-log, diagnostic-trace, stack-symbol, and source-provenance
paths; these are diagnostic artifacts rather than deployment outputs.

## Compiler support bundles

Use `--diagnostic-log <path>` when only the structured `ILogger` event stream
is needed. It writes flushed JSON Lines and is disabled when the option is
absent.

For a reproducible compiler report, use `--diagnostic-trace <path>` and pass
each relevant source with `--source <path>`. The compiler writes the primary
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
