# Configuration reference

Every NetWasm setting is an MSBuild property or item. Set a property in the
project file:

```xml
<PropertyGroup>
  <NetWasmOptimization>O3</NetWasmOptimization>
</PropertyGroup>
```

or for one build on the command line, as `dotnet publish -c Release
-p:NetWasmOptimization=O3`. The defaults below are what the SDK uses when the
project leaves a property unset. A property that depends on the configuration
says so; anything not listed behaves as in any other .NET project.

## What the project builds

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `TargetFramework` | set by the template | `net10.0-netwasm`. The older `netwasm0.1` still builds, with warning `NWSDK105`, and is removed in a later release. |
| `OutputType` | `Exe` | `Exe` builds a program with `Main`; `JsModule` builds C# exports that JavaScript calls; `WitWebWorker` builds a worker described by WIT; `Component` builds a component that exports a WIT world (such as an HTTP service); `CoreModule` builds a plain core module for an engine without the Component Model. See [choose the output kind](sdk-quickstart.md#choose-the-output-kind). |
| `NetWasmTarget` | `wasm32` | `wasm32` or `wasm64`, the address width. See [targets and output formats](targets-and-outputs.md). |
| `NetWasmRawWasm` | `false`; `true` for a `JsModule` | `true` builds a raw core module run by NetWasm's JavaScript host instead of a component. An `Exe` needs it only when its code imports application JavaScript modules with `[JSImport]`. |
| `NetWasmJavaScriptHosts` | `page` for a `JsModule` | Where a `JsModule` runs: `page`, `worker` or `node`, as a semicolon-separated list, each at most once. See [JavaScript interop](javascript-interop.md#choose-a-host). |
| `NetWasmWorld` | `command` for an `Exe` | The WIT world a `Component` or `CoreModule` exports, such as `service` for an HTTP service. |
| `NetWasmWitPath` | the toolchain's own package | The WIT package that holds `NetWasmWorld`, as an absolute path. |
| `NetWasmHttpServiceNamespace` | `$(RootNamespace)` | The namespace of the generated HTTP service entry point. See [write an HTTP service](sdk-quickstart.md#write-an-http-service). |
| `NetWasmPublishTarget` | `portable` | `portable` publishes for Node and other WASI hosts; `browser` publishes a page deployment with a generated `run()` module. |

## Optimization

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmOptimization` | `O0` in Debug, `Oz` otherwise | `None`, `O0`, `O1`, `O2`, `O3`, `Os` or `Oz`. `O1` to `O3` favor speed, `Os` and `Oz` size. Each level is a set of the switches in the [optimization reference](optimization.md), which a project can also set one by one. |
| `NetWasmOptimizerConverge` | `false` | `true` repeats Binaryen's passes until the module stops shrinking: a slower build for a slightly smaller module. |
| `NetWasmOptimizerLowMemoryUnused` | `false` | `true` lets Binaryen assume the first KiB of memory is unused, which NetWasm guarantees, and fold constant address offsets into loads and stores. |
| `NetWasmManagedInliningHints` | `Auto` | How `[MethodImpl(MethodImplOptions.AggressiveInlining)]` is treated: `Auto` follows the level, `Preserve` always honors it, `IgnoreAggressive` never does. |
| `NetWasmManaged*` | `Auto` | One switch per compiler optimization; see the [optimization reference](optimization.md). |
| `NetWasmGarbageCollector` | unset, which is `Compact` | `Compact`, `NetWasm`, `NetWasmGen` or `Bump`. See [choosing a garbage collector](runtime-garbage-collection.md). |
| `NetWasmCompiledUnits` | `true` at `O0` and `None`, `false` otherwise | `true` keeps each method's compiled code under `obj/` and recompiles only the methods an edit affects. |
| `NetWasmVerifyCompiledUnits` | `false` | `true` also compiles every reused method and fails the build if it differs. |

## Diagnostics

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmTypeNames` | `true` | Type names come from a sidecar file that a program loads only if it reads a name. `false` leaves them out: names read `type#<id>`, and looking a type up by name throws. |
| `NetWasmManagedStackTrace` | `true` in Debug, `false` otherwise | Managed stack traces with method names, and source lines where the build has them. |
| `NetWasmStructuredDiagnostics` | `true` in Debug, `false` otherwise | Rich failure reports from the host: the exception's type, message and stack trace as structured data. See [diagnostics](diagnostics.md#managed-exceptions-and-diagnostic-profiles). |
| `NetWasmMissingApiAsError` | `false` | A call to a member NetWasm lacks compiles to code that throws `MissingMethodException` or `MissingFieldException`, with warning `NW1016`. `true` makes `NW1016` an error. |
| `NetWasmValidateDeploymentManifest` | `false` in Release, `true` otherwise | Whether the JavaScript host checks the deployment manifest's schema when it loads. A Release deployment is checked by the build instead. |
| `NetWasmMinifyJavaScript` | `true` in Release, `false` otherwise | Whether the generated JavaScript is minified. |

## Host capabilities

A NetWasm program reaches files, the network, clocks and random numbers only
through what its host grants. These settings are the grants `dotnet run`,
`dotnet test` and a browser deployment's generated `run()` use. A host you
write passes its own.

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmGrantWorkingDirectory` | `true` | Mounts the working directory, read-write, at its own path. |
| `NetWasmGrantTemporaryDirectory` | `true` | Mounts a private temporary directory at `/tmp`, deleted after the run. |
| `NetWasmPreopen` item | none | Mounts another directory: `Include` is the host directory, `GuestPath` its absolute path inside the program, and `Access` is `readOnly` or `readWrite`. See [host capabilities](sdk-quickstart.md#host-capabilities). |
| `NetWasmNetworkPolicy` | `allowAll` | `allowAll` or `denyAll`, for sockets, DNS and `HttpClient`. |
| `NetWasmTlsSupport` | `true` | TLS through `wasi:tls`, which `SslStream`, `HttpClient` over HTTPS and `ClientWebSocket` need. A host without `wasi:tls` needs `false`; `SslStream` then throws `PlatformNotSupportedException`. |
| `NetWasmRandomness` | `true` | Grants the host's random number source. |
| `NetWasmClockGrant` item | `wall;monotonic` | The clocks granted: `wall`, `monotonic` or both. |
| `NetWasmEnvironmentVariable` item | none | An environment variable: `Include` is its name, `Value` its value. |
| `NetWasmArgument` item | none | A command-line argument passed to `Main`, in item order. |

```xml
<ItemGroup>
  <NetWasmEnvironmentVariable Include="TZ" Value="Australia/Melbourne" />
  <NetWasmArgument Include="--verbose" />
</ItemGroup>
```

## Time zones

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmTimeZone` item | none | A zone the deployment ships, such as `Australia/Melbourne`. Without zones, local time is UTC. |
| `NetWasmTimeZoneMode` | `Selected` when zones are listed | `Selected` ships the listed zones; `Full` ships every zone and takes no `NetWasmTimeZone` items. |

The program's zone is the `TZ` environment variable, which NetWasm never takes
from the host. See [time zone assets](timezone-assets.md).

## JavaScript, workers and WIT

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmJavaScriptModule` item | none | A JavaScript file that implements a `[JSImport]` module: `Include` is the file, `Module` the module name the C# code imports. See [choose the output kind](sdk-quickstart.md#choose-the-output-kind). |
| `NetWasmSiteFile` item | none | A file a browser deployment serves, declared during the build. See [run a browser project](sdk-quickstart.md#run-a-browser-project). |
| `NetWasmWit` item | none | A WIT file or package to generate C# bindings from: `World` selects the world, `Namespace` the C# namespace, and `Accessibility` is `Public` or `Internal`. See [WIT-to-C# bindings](wit-bindings.md). |
| `NetWasmWorkerClientClass`, `NetWasmWorkerClientNamespace` | from the worker project, in `$(RootNamespace).Workers` | Metadata on a worker `ProjectReference` that names its generated client. See [C# Web Workers](web-workers.md). |

## Feature switches

.NET's feature switches, such as `InvariantGlobalization`,
`UseSystemResourceKeys` and `EventSourceSupport`, work as in any .NET project,
and the code a switch turns off leaves the program. NetWasm's defaults differ
from desktop .NET's in a few places; see
[feature switches](sdk-quickstart.md#feature-switches).

## Toolchain

`dotnet restore` selects the compiler, Node, `wasm-ld`, `wasm-opt` and
`wasm-tools` from one host-tools package; nothing else needs installing.

| Setting | Default | Values and meaning |
| --- | --- | --- |
| `NetWasmCompilerPath` | the restored `nwc` | Another `nwc` executable, such as one built from source. |
| `NETWASM_NODE_PATH`, `NETWASM_WASM_LD_PATH`, `NETWASM_WASM_OPT_PATH`, `NETWASM_WASM_TOOLS_PATH` | the restored tools | Environment variables naming another executable by absolute path. An invalid one fails the build rather than falling back. |
