# CoreLib and runtime compatibility

Applications compile against `NetWasm.CoreLib`, not desktop .NET reference
assemblies. NetWasm performs closed-world reachability and links reachable
managed/runtime code with `NetWasm.Runtime`; a namespace or copied source file
does not guarantee that every member is supported.

See the [support status and roadmap](support-status.md) for the canonical
implementation and qualification inventory.

The retained profile covers selected primitive and enum operations, invariant
ordinal UTF-16 strings and formatting, arrays/spans, selected collections,
delegates/value types, closed generic specialization, exceptions and exact GC
roots, browser-event-loop `Task`/`ValueTask` and cancellation, selected date
and time types, direct JavaScript interop, WIT bindings, and selective WASI
Preview 2 clocks/readiness/streams.

### Binary floating-point to `decimal` policy

NetWasm selects the newer .NET 11-style conversion for `float`/`double` to
`decimal`: round the **exact binary input value** to the nearest representable
`decimal`, subject to `decimal` precision and range. This applies to casts,
`decimal` constructors, and `Convert.ToDecimal`. It does not mean that every
binary floating-point value is exactly representable as a `decimal`.

This rule is independent of the project's SDK or C# language version. In
particular, a project built with the .NET 10 SDK and C# 14 or earlier does
**not** select the older .NET 10 seven-significant-digit (`float`) or
fifteen-significant-digit (`double`) conversion rule. A desktop `net10.0`
oracle may therefore differ from NetWasm by design for these conversions;
that difference alone is not a compiler bug.

The implementation on this source branch passes its focused 16-cell
Debug/Release, wasm32/wasm64, direct/optimized, simulated/linked conversion
matrix against an independent exact-rational oracle. This branch result does
not imply that an already released SDK contains the change. The exception is
limited to these binary-to-`decimal` conversions and does not opt the rest of
CoreLib into .NET 11 semantics.

The following are intentionally outside the profile: broad reflection and
reflection-driven code generation, dynamic binding, runtime assembly loading,
runtime-created generic types, managed threads/workers/shared heaps, a managed
thread pool, blocking waits, and implicit browser capabilities. APIs that
require the single-reactor runtime to provide threads may throw
`PlatformNotSupportedException`; source compatibility alone is not runtime
support. Culture/ICU and timezone data are explicit optional capabilities, not
implicit CoreLib contents.

Abstract and in-memory streams do not imply a general filesystem:
`System.IO.File`, `FileStream`, `Directory`, enumeration/mutation/watching and
arbitrary preopen access are not implemented. The runtime's fixed read-only
timezone-sidecar mount is deployment plumbing, not a public file API. Selected
WASI HTTP does not expose `System.Net.Sockets`, raw TCP/UDP, listeners or
general DNS APIs. Selected CLI environment, arguments, output and exit plumbing
does not expose `System.Diagnostics.Process`, subprocess creation, shell
execution or process enumeration. The checked-in upstream WIT closure and host
provider catalog are not a .NET API inventory; see the
[first-preview WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).

`NetWasm.Runtime` owns native allocation, exact-GC integration, roots, managed
exceptions, finalization, metadata, and target-specific memory support. Raw
JavaScript owns JavaScript objects/subscriptions; a Component host owns its
declared WIT/WASI imports. Runtime ABI imports are implementation mechanics,
not public Component services. Do not exchange raw pointers or assume wasm32
addresses when targeting wasm64.

The packaged native runtime is relocatable and linked per application. The
application's static-data end determines the aligned runtime base, and the
linked runtime footprint determines the heap base. The current policy uses a
fixed 64 KiB native stack, a 64 KiB default initial heap, and 64 KiB
linear-memory pages. The runtime manifest validates 4-byte wasm32 and 8-byte
wasm64 pointers and selects the matching link machine; current maximum-memory
policy is 2 GiB for wasm32 and 8 GiB for wasm64. These are policy limits, not
pre-reserved runtime tiers.

## Source and license boundary

The compiler, CLI, Component Model tooling, linker, optimizer, debugger, IDE,
and build/developer tooling are covered by the NetWasm Community License 1.0.
`NetWasm.CoreLib`, `NetWasm.Runtime`, and adjacent framework ports are MIT,
with preserved upstream notices where applicable. See the [licensing
guide](licensing.md).
