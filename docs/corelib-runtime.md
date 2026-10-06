# CoreLib and runtime compatibility

Applications compile against `NetWasm.CoreLib`, not desktop .NET reference
assemblies. NetWasm performs closed-world reachability and links reachable
managed/runtime code with `NetWasm.Runtime`; a namespace or copied source file
does not guarantee that every member is supported.

See the [support status and roadmap](support-status.md) for the canonical
implementation and qualification inventory, and the
[behavior-differences ledger](spec-deviations.md) for deliberate profile
choices and standards-permitted results.

The retained profile covers selected primitive and enum operations, invariant
ordinal UTF-16 strings and formatting, arrays/spans, selected collections,
delegates/value types, the bounded
[expression-tree interpreter](expression-trees.md), closed generic
specialization, exceptions and exact GC roots, browser-event-loop
`Task`/`ValueTask` and cancellation, selected date and time types, direct
JavaScript interop, WIT bindings, and selective WASI Preview 2
clocks/readiness/streams.

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
that difference alone is not a compiler bug. It is also recorded in the
[behavior-differences ledger](spec-deviations.md).

The implementation on this source branch passes its focused 16-cell
Debug/Release, wasm32/wasm64, direct/optimized, simulated/linked conversion
matrix against an independent exact-rational oracle. This branch result does
not imply that an already released SDK contains the change. The exception is
limited to these binary-to-`decimal` conversions and does not opt the rest of
CoreLib into .NET 11 semantics.

The candidate [custom attribute query profile](custom-attributes.md) resolves
one closed target and filter during compilation, including user-defined
attributes. Candidate qualification passes; release delivery is pending. It does not enable general runtime
reflection or unfiltered metadata enumeration.

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

Starting with 0.7.0, Compact is the default precise, non-moving garbage collector;
Boehm remains selectable at build time. See
[Choosing a garbage collector](runtime-garbage-collection.md) for the tradeoffs
and application project setting. Class libraries remain collector-neutral.

The packaged native runtime is relocatable and linked per application. The
application's static-data end determines the aligned runtime base, and the
linked runtime footprint determines the heap base. The current policy uses a
fixed 64 KiB native stack, a 64 KiB default initial heap, and 64 KiB
linear-memory pages. The runtime manifest validates 4-byte wasm32 and 8-byte
wasm64 pointers and selects the matching link machine; current maximum-memory
policy is 2 GiB for wasm32 and 8 GiB for wasm64. These are policy limits, not
pre-reserved runtime tiers.

## Static native interop

The bounded static C implementation is available in NetWasm 0.6.0 and has been
qualified across ordinary application, library and test consumers on wasm32
and raw wasm64. See the [two-way tutorial](static-native-interop.md)
and [support inventory](support-status.md).

Use ordinary source-generated `LibraryImport` or supported raw `DllImport`
declarations on non-generic static methods in non-generic declaring types. The
SDK links regular Wasm `.a` archives into the application, not dynamically loaded
libraries. Logical names need no `.a` suffix and match an explicit target-specific
provider ordinally. Set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for
the stock `LibraryImport` source generator:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>

<ItemGroup>
  <NativeLibrary Include="native/wasm32/libexample.a"
                 NetWasmLibraryName="example"
                 WasmTarget="wasm32" />
</ItemGroup>
```

All three fields are required. Source project references propagate declarations;
NuGet library authors package assets with ordinary package-relative
`buildTransitive` declarations. The final application selects its width and
host. Library assets do not grant permissions or select a host. Identical
contributions may coalesce; conflicting reached providers fail. Logical names
do not isolate the native symbol namespace.

| Supported call shape | Boundary |
| --- | --- |
| `int`/`uint`, `long`/`ulong`, `float`/`double` | Raw C scalar parameters/results; `void` results are supported |
| `nint`/`nuint`, unmanaged data pointers | Guest width, 32 or 64 bits; not interchangeable with fixed-width handles |
| Scalar `ref`/`out` | Call-scoped address; native code must not retain managed storage |
| By-value structs and unions | Unmanaged sequential/explicit layouts, including nested values, packed narrow fields, fixed buffers, explicit padding and qualified empty root values |
| Counted arrays | Qualified byte, integer and aggregate-element input, output and in-out arrays with explicit count/capacity; storage is caller-owned and call-scoped |
| UTF-8 strings | Stock source-generated input marshalling, including null, empty, non-ASCII and embedded-NUL behavior; native code must not retain the buffer |
| `SafeHandle` inputs | Borrowed for the call with lifetime protection; ownership, repeated/reentrant disposal and finalization preserve exact release behavior |
| Static callbacks | Address-taken or named `UnmanagedCallersOnly` methods with the supported scalar/pointer signatures, default C ABI or Cdecl, explicit registration lifetime and `nint` user tokens |

Default C ABI and Cdecl import flags are supported without additional semantics.
Stock generated code is ordinary CIL, not a compiler-specific generator path;
generation does not make an unsupported raw leaf or helper available.

The aggregate profile does not introduce a raised-alignment attribute:
`StructLayout.Pack` can lower alignment but cannot express an over-aligned C
type. Embedded empty aggregates are rejected; a qualified empty root value uses
the documented logical-to-physical adaptation. Only defined fields and bits are
portable across a union boundary. SafeHandle `ref`, `out` and return construction
remain unsupported because the stock helper requires reflection-based generic
activation. Native-owned arrays and retained array/string pointers are outside
the profile.

Callback signatures remain scalar/pointer only. Callback aggregates, arrays,
byrefs, SafeHandle values and callback-valued parameters/results are rejected.
Arbitrary unmanaged `calli`, delegate-to-native-pointer conversion, dynamic
trampolines, worker-thread callbacks and constructor-time managed entry remain
unsupported. A named callback requires normal managed application startup
before native entry.

Broad runtime marshalling, raw bool/char/string leaves, narrow raw scalars,
byref returns, varargs, custom calling conventions, custom marshallers,
`SetLastError` and `SuppressGCTransition` remain outside the profile. Dynamic
libraries, thin archives and direct consumer `.o` inputs are rejected. Object
and compatible LTO archive members must match guest width and the qualified
Wasm feature set. Native threads, direct C++ ABI and native C++ exceptions
crossing managed frames remain outside the profile.

Reached calls select exact providers and entry symbols. Unreached declarations
do not retain their archives or enable native linking. Consumer archives are not
linked whole-archive. Archive bytes participate in materialization identity:
changing bytes at the same path invalidates the link, not reusable CIL frontend
facts. Native linked data, stack and heap bounds determine the memory plan and
are validated again on cache hits.

Native code shares guest linear memory but its storage is not automatically a
GC root. Outward calls conservatively preserve managed roots. Callers must
manage pointer lifetimes, and native initialization must not reenter managed
code. Linked dependencies must fit the application's declared host capabilities,
with no Preview 1 fallback or implicit browser/desktop service. wasm64 uses its
explicit raw module path while Memory64 Component packaging remains blocked.

Normal SDK consumers restore packaged build tools and prebuilt archives. They
do not need emsdk, Python or manual link commands. Internal build-tool execution
has no P2 requirement and is independent of the application's ABI.

## Source and license boundary

The compiler, CLI, Component Model tooling, linker, optimizer, debugger, IDE,
and build/developer tooling are covered by the NetWasm Community License 1.0.
`NetWasm.CoreLib`, `NetWasm.Runtime`, and adjacent framework ports are MIT,
with preserved upstream notices where applicable. See the [licensing
guide](licensing.md).
