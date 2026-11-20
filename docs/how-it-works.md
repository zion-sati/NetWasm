# How NetWasm works

NetWasm compiles a .NET program ahead of time into one WebAssembly module. It
does not ship a .NET runtime that loads assemblies: the program, the parts of
the framework it uses, its garbage collector and its host interface are all
compiled and linked into the `.wasm` file. This page follows a build from C#
to a deployment. [Compiler architecture](ARCHITECTURE.md) describes the
compiler's own structure, for contributors.

## From C# to Wasm

1. **Roslyn** compiles C# to CIL, as for any .NET project, against NetWasm's
   framework, NetWasm.App, instead of Microsoft.NETCore.App.
2. **`nwc`**, the NetWasm compiler, reads the CIL of the program and of every
   assembly it references. Starting from the entry points (`Main`, the
   `[JSExport]` methods, a WIT world's exports), it finds the code the program
   can reach and compiles only that. Each generic method and type is compiled
   for the type arguments the program uses.
3. **Optimization.** At the level the build selects, `nwc` optimizes the
   managed code, for example by inlining and by turning short branches into
   selects, then emits Wasm with exact garbage-collection roots: the collector
   knows which values are references, so it never scans memory
   conservatively. See [optimization](optimization.md).
4. **Linking.** `wasm-ld` links the compiled program with the runtime support it
   uses, from NetWasm's runtime pack: the chosen
   [garbage collector](runtime-garbage-collection.md), the C runtime and any
   [native C libraries](static-native-interop.md) the project adds. Only one
   collector is linked.
5. **Binaryen's `wasm-opt`** optimizes the linked module.
6. **Packaging.** For a WASI host, `wasm-tools` wraps the module in a WASI 0.3
   component. For JavaScript hosts, the module stays a core module and the
   build generates the JavaScript that loads it.

The compiler, Node.js, `wasm-ld`, Binaryen and `wasm-tools` come from one
host-tools NuGet package for the build machine, at versions NetWasm pins.

## The framework

`NetWasm.CoreLib` is .NET's `System.Private.CoreLib`, ported: .NET's own source
wherever WebAssembly allows, with NetWasm's runtime underneath, so strings,
collections, generics, exceptions and `Task` behave as on .NET 11. Other
framework assemblies, such as `System.Net.Http`, are .NET's own libraries
compiled for NetWasm. System services (files, sockets, HTTP, clocks) go through
WASI 0.3; see [host capabilities](host-capabilities.md).

What the program does not reach is not in the output, including runtime
features: a program that never reads a type name carries no type names, and one
that never opens a file carries no file-system code. That closed world is also
the profile's boundary. Runtime reflection over members, `dynamic`, loading
assemblies at run time and managed threads are not available, because they need
code the compiler cannot see in advance. Most such calls still compile, and
throw `PlatformNotSupportedException` when they run. See
[support status](support-status.md) and
[differences from desktop .NET](spec-deviations.md).

## Exceptions and memory

.NET exceptions compile to WebAssembly's exception-handling instructions.
Objects live in the module's linear memory, managed by the linked collector; the
native stack is fixed at 64 KiB and the heap grows as the host allows. Programs
are single-threaded: `async` code runs on one thread and resumes when the host
completes its I/O.

## Running in JavaScript

For a page, a worker or Node.js code, the build writes a JavaScript module whose
`open()` (for a `JsModule`) or `run()` (for a program) loads the deployment: the
`.wasm` file, its manifest and NetWasm's JavaScript host. Startup checks every
file against the SHA-256 digests in the manifest, compiles the module, and
connects the program's `[JSImport]` declarations to JavaScript functions. Each
`[JSExport]` method becomes a JavaScript function. See
[JavaScript interop](javascript-interop.md) and [security](security.md).

## Debug builds

Debug builds compile at `O0`, keep managed stack traces, and keep each method's
compiled code under `obj/`. The next build recompiles only the methods an edit
affects, and when an edit changes only method bodies, it patches the last
module instead of compiling the program again.
