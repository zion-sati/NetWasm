# Current limitations

NetWasm is experimental. The supported API surface is intentionally smaller
than desktop .NET and must not be inferred from a namespace or type name alone.
Treat successful compilation and the focused tests in this repository as the
current evidence of support.

## Platform and language boundary

- Applications compile against `NetWasm.CoreLib`, not desktop .NET reference
  assemblies.
- Compilation is closed-world. Generic specialization is monomorphized, so
  runtime-created generic types are unsupported.
- Broad reflection, reflection-driven code generation, and `dynamic` are
  unsupported.
- Managed threads, worker execution, and a managed thread pool are unsupported.
- Browser-specific capabilities such as DOM, input, fetch and storage are
  only available through explicit JavaScript imports in raw-core mode; they are
  not implicit component or WASI services.

## Runtime and component boundary

- The CoreLib profile remains incomplete and intentionally differs from desktop
  .NET.
- The checked-in WASI 0.2.11 WIT closure and Hosting provider catalog are not a
  public .NET API inventory. They let the compiler and host describe exact
  interfaces; only reachable imports backed by an implemented managed contract
  are supported.
- General filesystem APIs are missing. Abstract and in-memory streams are
  available, and local-time support can consume one fixed read-only timezone
  sidecar, but `System.IO.File`, `FileStream`, `Directory`, enumeration,
  mutation, watching and arbitrary preopen access are not implemented. Public
  file APIs are deferred until after the WASI 0.3 migration.
- Raw networking APIs are missing. The selected `System.Net.Http` profile uses
  WASI HTTP, but `System.Net.Sockets`, raw TCP/UDP, listeners and general DNS
  APIs are not implemented as managed APIs, even if a host provider contains
  the corresponding WASI interfaces.
- Process creation and discovery are missing. Selected CLI arguments,
  environment, output and exit plumbing do not provide
  `System.Diagnostics.Process`, subprocess creation, shell execution or process
  enumeration.
- Concurrency remains single-reactor. `Task`, `ValueTask`, cancellation, timers
  and readiness continuations are available; managed threads, shared-heap
  workers, a thread pool, `Task.Run` parallelism, blocking waits and
  shared-memory/atomic threading semantics are not.
- User-defined WIT imports and exports are synchronous in the current profile.
  JavaScript Promise interop uses the explicit JavaScript boundary.
- Component Model and WASI support is selective. A component must declare the
  WIT/WASI capabilities it consumes.
- NetWasm can emit and validate wasm64 **core modules**, but cannot currently
  package them as WebAssembly Components. The stable component encoder,
  validator, native runtime, and browser-tooling chain does not yet provide an
  end-to-end memory64 Component Model path. A wasm64 component request fails
  immediately with `NW1010`; NetWasm never silently downgrades it to wasm32.
  Use wasm32 for component output or use wasm64 only through the explicit
  raw-core host contract on a Memory64-capable host.
- WASI 0.3 does not remove this limitation. A wasm32 transport evolution is
  independent of `cm64p2`: it neither solves nor waits on the separate Memory64
  Component Model canonical-ABI and toolchain capability.
- The documented flow is the Component Model path. Raw-core output has a
  different host contract and is for explicit host-specific integration.

## Compatibility and release status

- Released SDK/template packages support the experimental `netwasm0.1`
  profile. A release version is not a production-support or future-compatibility
  guarantee.
- Changes to CIL support, CoreLib APIs, runtime ABI, generated manifests, and
  build output remain possible.
- The repository is not a production deployment recommendation.

When evaluating a library, inspect its CIL and API dependencies and validate it
against the desired target. Do not assume that a desktop NuGet package works
because it compiles for another .NET deployment model.

The [CoreLib/runtime reference](corelib-runtime.md) is the detailed
compatibility boundary. See [targets and output formats](targets-and-outputs.md)
for the wasm32/wasm64 and Component Model matrix, and [compiler diagnostics](diagnostics.md)
for actionable rejection codes.
