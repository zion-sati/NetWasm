# Current limitations

NetWasm is pre-1.0. The supported API surface is intentionally smaller than
desktop .NET and must not be inferred from a namespace or type name alone. Treat
successful compilation and the focused tests in this repository as the current
evidence of support.

## Platform and language boundary

- Applications compile against `NetWasm.CoreLib`, not desktop .NET reference
  assemblies.
- Compilation is closed-world. Generic specialization is monomorphized, so
  runtime-created generic types are unsupported.
- Broad reflection, reflection-driven code generation, and `dynamic` are
  unsupported.
- Expression-tree execution is a bounded managed-interpreter profile, not
  runtime code generation or desktop parity. See the exact
  [delegate and node matrix](expression-trees.md).
- Managed threads, worker execution, and a managed thread pool are unsupported.
- Browser-specific capabilities such as DOM, input, fetch and storage are
  only available through explicit JavaScript imports in raw-core mode; they are
  not implicit component or WASI services.

## Runtime and component boundary

- The CoreLib profile remains incomplete and intentionally differs from desktop
  .NET.
- The checked-in WASI 0.3.2 WIT closure and Hosting provider catalog are not a
  public .NET API inventory. They let the compiler and host describe exact
  interfaces; only reachable imports backed by an implemented managed contract
  are supported.
- Files reach only the directories the host grants. `System.IO.File`,
  `FileStream`, `Directory` and their relatives work over WASI 0.3's
  filesystem within the granted directories (`dotnet run` grants the working
  directory and a private temporary directory; a published command takes
  `--dir`); a browser page or worker mounts files with NetWasm's mount API.
  Permissions, ownership, locking and watching are not available.
- Networking is a WASI 0.3 subset. `System.Net.Sockets` (TCP, UDP, listeners),
  `Dns`, `ws://` WebSockets and `HttpClient` work where the host grants the
  network, but sockets cannot be made non-blocking, socket timeouts, `Poll` and
  `Select`, Unix domain sockets, multicast and broadcast, binding a dual-mode
  socket to the IPv6 wildcard, and reverse DNS are not available, and `wss://`
  waits for WASI's TLS interface.
- Process creation and discovery are missing. Selected CLI arguments,
  environment, output and exit plumbing do not provide
  `System.Diagnostics.Process`, subprocess creation, shell execution or process
  enumeration.
- Concurrency remains single-reactor. `Task`, `ValueTask`, cancellation, timers
  and readiness continuations are available, and synchronous I/O APIs work over
  WASI's asynchronous operations on hosts that can suspend (Wasmtime, Spin,
  wasmCloud, NetWasm's Node.js launcher). In a JavaScript host that cannot
  suspend the guest, such as a `JsModule`'s page or worker, a call that must
  wait for I/O (`Console.ReadLine`, a synchronous socket call) throws
  `PlatformNotSupportedException`; use the async API there. File APIs never wait in NetWasm's JavaScript hosts; managed threads,
  shared-heap workers, a thread pool, `Task.Run` parallelism, blocking waits on
  a task and shared-memory/atomic threading semantics are not.
- User-defined WIT functions may be `async func`, bound to `Task`; `stream<T>`
  and `future<T>` bind to streams, `IAsyncEnumerable<T>` and `Task<T>`.
  JavaScript Promise interop uses the explicit JavaScript boundary.
- Component Model and WASI support is selective. A component must declare the
  WIT/WASI capabilities it consumes.
- WASI needs an engine that implements the Component Model and WASI 0.3, or
  NetWasm's own Node.js and browser hosts. NetWasm never uses WASI Preview 1.
  An engine without the Component Model can load a plain core module, which
  imports only its world's functions and has no WASI: .NET APIs that need a
  system service throw `PlatformNotSupportedException` there.
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

- Released SDK/template packages support the current `netwasm0.1` profile. A
  0.x release does not carry the 1.x compatibility commitment described in the
  root roadmap.
- Changes to CIL support, CoreLib APIs, runtime ABI, generated manifests, and
  build output remain possible.
- There is no service-level agreement or independent security audit; deployments
  must validate the exact supported surface and host capabilities they use.

When evaluating a library, inspect its CIL and API dependencies and validate it
against the desired target. Do not assume that a desktop NuGet package works
because it compiles for another .NET deployment model.

The [CoreLib/runtime reference](corelib-runtime.md) is the detailed
compatibility boundary. The
[language, CLI, and .NET behavior differences](spec-deviations.md) ledger
records deliberate profile differences and standards-permitted results. See
[targets and output formats](targets-and-outputs.md) for the wasm32/wasm64 and
Component Model matrix, and [compiler diagnostics](diagnostics.md) for
actionable rejection codes.
