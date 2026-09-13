# NetWasm architecture

NetWasm accepts managed assemblies produced by Roslyn and compiles a closed
world rather than loading assemblies into a general-purpose runtime at startup.
The compiler starts from an explicit entry point, follows reachable code and
metadata, specializes concrete generic uses, and emits a WebAssembly module
with the runtime support that remains reachable.

## Compilation pipeline

```text
Managed assemblies + NetWasm.CoreLib
                |
                v
Metadata and CIL decoding
                |
                v
Validation, control-flow structurization, reachability and layout
                |
                v
Wasm planning and binary emission
                |
                +--> Component Model packaging through WIT and WASI Preview 2
                |
                `--> raw core Wasm with a generated interop manifest
```

`NetWasm.Compiler.Core` contains the CIL, type-system and ABI models.
`NetWasm.Compiler.Metadata` reads ECMA-335 metadata and CIL;
`NetWasm.Compiler.ControlFlow` validates and structurizes method bodies;
`NetWasm.Compiler.GarbageCollection` performs exact-root analysis; and
`NetWasm.Compiler.Wasm` plans and encodes Wasm. `NetWasm.Compiler` coordinates
whole-program analysis, specialization, layout, and compilation.

`NetWasm.Compiler.ComponentModel` supplies WIT parsing and binding, Canonical
ABI generation, manifests, linking, and component packaging. The CLI is the
current command-line entry point.

## Runtime boundary

`NetWasm.CoreLib` is the managed platform library compiled with an application.
`NetWasm.Runtime` provides the native runtime support used by both output
forms. The current documented application flow packages a component with WIT
and WASI Preview 2. A component declares the capabilities it consumes; it does
not receive implicit platform access.

Raw-core output remains available for explicit host-specific JavaScript
integration. Its boundary is manifest-driven: the generated adapter receives
consumer-supplied JavaScript modules rather than implicitly exposing browser
APIs. The raw-core and component paths have different contracts.

## Repository boundaries

The compiler, linker, optimizer, and its build/developer, debugger, and
IDE/browser tooling are covered by the NetWasm Community License 1.0. CoreLib,
runtime, and adjacent framework ports are MIT, subject to their preserved
upstream notices. See [the license map](../LICENSE-MAP.md) and [licensing
guide](licensing.md).

This document describes the current implementation shape, not a long-term ABI
or package-compatibility promise.

See the [documentation index](index.md), [targets and output formats](targets-and-outputs.md),
[CoreLib/runtime compatibility reference](corelib-runtime.md),
[CLI/build/deployment reference](cli-build-deployment.md), and
[compiler diagnostics](diagnostics.md) for the user-facing contracts.
