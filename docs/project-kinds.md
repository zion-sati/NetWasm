# What a project can build

A NetWasm project is an ordinary .NET project whose SDK is `NetWasm.Sdk` and
whose target framework is `net10.0-netwasm`. `OutputType` decides what it
builds. This page shows each kind, the template that starts one and what
`dotnet publish` produces.

| You want | Start with | `OutputType` | Output |
| --- | --- | --- | --- |
| A program with `Main` for Node.js or a WASI host | `dotnet new netwasm-app` | `Exe` (the default) | A WASI 0.3 command component |
| C# that a web page calls | `dotnet new netwasm-app --host page` | `JsModule` | Raw Wasm with a generated `open()` module |
| C# that runs off the page's thread | `dotnet new netwasm-app --host worker` | `JsModule` | The same, run in a Web Worker, with a generated client |
| A worker described by WIT | `dotnet new netwasm-app --worker wit` | `WitWebWorker` | A component with generated WIT bindings |
| C# that Node.js code calls | `NetWasmJavaScriptHosts=node` | `JsModule` | A Node.js module with `open()` |
| An HTTP service | `dotnet new netwasm-serverless` | `Component` | One `wasi:http` 0.3 component |
| A component for any WIT world | `NetWasmWorld` and a `NetWasmWit` item | `Component` | One WASI 0.3 component |
| A module for an engine without the Component Model | `NetWasmWorld` | `CoreModule` | One plain core module with no WASI |
| A library for NetWasm and desktop .NET | `dotnet new netwasm-lib` | `Library` | A NuGet package for `net10.0-netwasm` and `net10.0` |

## A program

```bash
dotnet new netwasm-app -n Hello
cd Hello
dotnet run
```

`Main` can be synchronous or asynchronous (`Task Main`, `Task<int> Main`): the
host waits for the returned task and uses its exit code. `dotnet run` runs the
program in Node.js through NetWasm's launcher. `dotnet publish` writes the
component and the launcher to the publish directory; run it with
`node netwasm.host.mjs`, or run the `.wasm` file in any WASI 0.3 host, such as
`wasmtime run`. What the program can reach (files, network, clocks) is what the
host grants; see [host capabilities](host-capabilities.md).

A program that calls JavaScript modules it brings itself, with `[JSImport]`,
needs raw output instead of a component: set `NetWasmRawWasm` to `true` and
declare each module with a `NetWasmJavaScriptModule` item. Async code alone is
not a reason to set it. See
[choose the output kind](sdk-quickstart.md#choose-the-output-kind) for the
details.

## C# called from JavaScript

A `JsModule` has no `Main`. Its `[JSExport]` methods are the API, and the
build generates a JavaScript module whose `open()` loads the program and
returns those methods:

```js
import { open } from "./browser/Calculator.mjs";

const app = await open();
console.log(app.Add(2, 3));
await app.close();
```

`NetWasmJavaScriptHosts` chooses where it runs: `page` (the default),
`worker`, `page;worker` or `node`. A page export returns its value, or a
Promise for a `Task`; every worker export returns a Promise. Each `open()`
starts an independent instance that stays loaded until `close()`, so state in
C# statics carries from one call to the next.
[JavaScript interop](javascript-interop.md) covers the types that
cross, callbacks, cancellation and the generated TypeScript declarations;
[C# Web Workers](web-workers.md) covers the worker client.

## Workers

`--host worker` runs a `JsModule` in a Web Worker and generates
`browser/<AssemblyName>.worker-client.mjs`, whose `createWorker()` returns the
exports as Promise-returning methods. `--worker wit` builds a `WitWebWorker`,
a worker whose interface is a WIT world. Both are described in
[C# Web Workers](web-workers.md), including notifications to the page,
`dispose()` and `terminate()`.

## Components

`Component` builds one WASI 0.3 component that exports a WIT world. The
`service` world is an HTTP service: one method takes an `HttpRequestMessage`
and returns an `HttpResponseMessage`; see [HTTP services](http-services.md).
For any other world, generate its C# bindings with a `NetWasmWit` item and
implement them; see [WIT and components](wit-bindings.md). Components run in
any host that supports WASI 0.3, such as Wasmtime, Spin and wasmCloud.

`CoreModule` builds the same program as a plain core module, for an engine
that runs core WebAssembly but not the Component Model. It has no WASI
imports.

## Libraries

`dotnet new netwasm-lib` creates a library that targets `net10.0-netwasm` and
`net10.0`, so one package serves NetWasm applications and desktop .NET. A
library selects no host: the application that uses it does. Most libraries
need no NetWasm target at all: a `net10.0-netwasm` application restores a
package's `net10.0`, `net9.0`, `net8.0` or .NET Standard build, newest first;
see [packages and libraries](netstandard-libraries.md).

## Target framework and address width

`net10.0-netwasm` is .NET's own target framework on the `netwasm` platform, as
`net10.0-browser` is for Blazor. The project compiles against NetWasm's
framework, NetWasm.App, not Microsoft.NETCore.App. The older `netwasm0.1`
still builds, with warning `NWSDK105`, and will be removed; change
`TargetFramework` to `net10.0-netwasm` to upgrade.

Programs are `wasm32` by default. `NetWasmTarget=wasm64` builds Memory64 output
for raw modules; components are `wasm32` only. See
[targets and output formats](targets-and-outputs.md).
