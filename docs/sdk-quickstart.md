# NetWasm SDK quickstart

Before porting an application, review the
[first-preview WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).
The first preview has no general managed file/directory API, raw socket API,
subprocess API, or managed threading/thread-pool runtime. Its in-memory streams,
selected WASI HTTP and CLI contracts, async reactor, and read-only timezone
mount are deliberately narrower capabilities.

## 1. Install .NET

Install [.NET SDK 10.0.303 or newer](https://dotnet.microsoft.com/download/dotnet/10.0).
NetWasm's own repository builds with the .NET 11 SDK. SDK 10.0.302
and earlier carry a [CoreCLR ARM64 thread-static publication race](https://github.com/dotnet/runtime/issues/127776)
that can terminate parallel compilation with `SIGSEGV`; Microsoft shipped the
[fix](https://github.com/dotnet/runtime/pull/127843) in runtime 10.0.11.

Linux distributions package only the 10.0.1xx SDK (for example `pacman -S
dotnet-sdk` on Arch, `dnf install dotnet-sdk-10.0` on Fedora and RHEL), and
its NuGet cannot restore a `netwasm0.1` project. On those distributions,
install Microsoft's SDK with the
[install script](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual)
instead. A NetWasm project built with an older SDK stops with `NWSDK086`.

The supported development hosts are Linux x64/ARM64, macOS ARM64 and Windows
x64/ARM64. On Windows ARM64, use the native ARM64 .NET SDK; the x64 SDK under
emulation cannot select the ARM64 host-tools package.
The pinned Linux tools need glibc 2.28 or newer; the Linux package carries
Node's `libatomic.so.1` dependency. Musl-based distributions such as Alpine
are not supported development hosts and stop with `NWSDK041`. The pinned
macOS Node binary needs macOS 13.5 or newer.

Projects from the templates build with whichever .NET SDK `dotnet` selects:
the newest one installed, unless a `global.json` you add chooses another.
NetWasm accepts SDK 10.0.303 or newer, including .NET 11; to compile C# 15
source, use the .NET 11 SDK version recorded in
[`eng/csharp15-toolchain.json`](../eng/csharp15-toolchain.json). An isolated
.NET 11 SDK installation is sufficient; NetWasm does not also require the
.NET 10 SDK. The target framework remains `netwasm0.1`.

SDK selection does not change NetWasm's `float`/`double` to `decimal`
rounding rule. Even with the .NET 10 SDK and C# 14 or earlier, NetWasm selects
the [newer exact-binary-input conversion](corelib-runtime.md#binary-floating-point-to-decimal-policy),
not desktop .NET 10's seven/fifteen-significant-digit rule. The linked
compatibility note describes the qualified source-branch behavior; check the
release containing this change before relying on it in a published SDK.

For an ordinary application or test project, no separate Emscripten, Node.js,
LLVM/LLD, Binaryen, Git or Python installation is needed. `dotnet restore`
selects one host-tools NuGet package for the machine doing the build. That
package supplies `nwc`, the NetWasm compiler, as a native executable, and the
pinned Node, `wasm-ld`, `wasm-opt` and `wasm-tools` tools. The first restore
downloads the package (about 90 MB compressed on macOS ARM64, plus the other
NetWasm dependencies); NuGet caches it for subsequent
projects. Package sizes differ by host. Builds do not download tools.

After a successful restore, `dotnet build --no-restore`, `dotnet run
--no-build --no-restore` and `dotnet test --no-build --no-restore` can use the
cached packages offline. `dotnet clean` leaves the shared NuGet cache intact.
Wasmtime remains optional and is not needed for the standard SDK build, run or
publish journey.

## 2. Install the templates

```bash
dotnet new install NetWasm.Templates
```

## 3. Create and run an application

```bash
mkdir HelloNetWasm
cd HelloNetWasm
dotnet new netwasm-app
dotnet restore
dotnet build
dotnet run
```

The template prints `42`. Debug builds include managed stack traces by default.
Release omits their instrumentation and method-symbols sidecar unless
`NetWasmManagedStackTrace=true`, and reads type names as `type#<id>` unless
`NetWasmTypeNames=true`, which looking a type up by name, as `Type.GetType(string)`
does, needs (see [type names](support-status.md#type-names)).
Structured rich failure reporting also defaults on in Debug and off in Release,
with the independent `NetWasmStructuredDiagnostics` switch. See
[exception profiles and their non-desktop boundaries](diagnostics.md#managed-exceptions-and-diagnostic-profiles).

Debug builds compile at `O0` and keep each method's compiled code under `obj/`, so
the next build compiles only the methods an edit affects. A kept method is reused
only while everything its code was built from still holds. When an edit changes only
method bodies and each edited method still uses what it used before, the build patches
the last module with those methods instead of compiling the program again. Set
`NetWasmCompiledUnits=false` to compile every method each time,
`NetWasmCompiledUnits=true` to keep compiled code at other levels too, or
`NetWasmVerifyCompiledUnits=true` to also compile every reused method and fail the
build if it differs.

Release builds optimize at `Oz` unless `NetWasmOptimization` names another level.
Binaryen's `wasm-opt` runs the level's passes once over the linked module. Set
`NetWasmOptimizerConverge=true` to repeat them until the module stops shrinking:
on DoomSharp that took the optimizer about twice as long for a module 0.04% (`Oz`)
to 0.12% (`O3`) smaller, and rendered no faster. A later round can also remove
calls to `NoInlining` methods that an earlier round found empty, which makes some
micro-benchmarks up to three times faster. The published benchmarks build with
it, as a CI release build can afford to.

NetWasm places nothing in the first KiB of memory, so at every level that runs
`wasm-opt` it lets Binaryen assume that memory is unused, as Emscripten's optimized
builds do. Binaryen then folds a constant added to an address into the load or
store that uses it. Set `NetWasmOptimizerLowMemoryUnused=false` to turn that off.

An application or runnable test project restores one host-tools package. A
plain or dual-target library does not; its consumer selects the host package.
If you move a restored checkout to another development host, run `dotnet
restore` again. An ambient `node`, `wasm-ld` or Binaryen installation does not
replace the restored defaults. For an intentional override, set
`NETWASM_NODE_PATH`, `NETWASM_WASM_LD_PATH`,
`NETWASM_WASM_OPT_PATH` or `NETWASM_WASM_TOOLS_PATH` to the corresponding
executable's absolute path. A `wasm-tools` override must be the toolchain's own
release.
Invalid overrides fail validation rather than falling back silently.

If you enable NuGet lock files with `RestorePackagesWithLockFile=true`, the SDK
uses a host-qualified name such as `packages.linux-x64.lock.json` for an app or
test project. Commit the files for the development hosts you support. A
checkout with an older `packages.lock.json` can run an unlocked restore to
generate the new host file, then review and remove the old file. To keep a
deliberate custom name, set `NuGetLockFilePath` explicitly; one explicit lock
file shared across different hosts must be regenerated for each host.

### Choose the output kind

An application's `Main` can be synchronous or asynchronous (`Task Main`,
`Task<int> Main`) without any setting: the host waits for the returned task
and uses its exit code. What a project builds is chosen by `OutputType` and,
for application-owned JavaScript imports, `NetWasmRawWasm`:

| What you are building | Project setting | Output |
| --- | --- | --- |
| A program with `Main` | `OutputType=Exe` (the default) | A WASI 0.3 command component |
| A program with application-owned `[JSImport]` modules | `OutputType=Exe` plus `NetWasmRawWasm=true` | Raw, with the NetWasm JavaScript host |
| C# exports that JavaScript calls, with no `Main` | `OutputType=JsModule` and `NetWasmJavaScriptHosts` | Raw, with a generated `open()` module |
| A worker described by WIT | `OutputType=WitWebWorker` | A component with generated WIT bindings |
| A component that exports a WIT world, such as an HTTP service | `OutputType=Component` and `NetWasmWorld` | One WASI 0.3 component, `<AssemblyName>.wasm` |
| A module for an engine without the Component Model | `OutputType=CoreModule` and `NetWasmWorld` | One plain core module with no WASI |

Async code alone is not a reason to set `NetWasmRawWasm=true`. For example, an
entry point that directly imports an application JavaScript module selects
raw output because of that import:

```xml
<PropertyGroup>
  <NetWasmRawWasm>true</NetWasmRawWasm>
</PropertyGroup>
```

```csharp
using System.Runtime.InteropServices.JavaScript;

internal static class Host
{
    [JSImport("report", "app.host")]
    internal static extern void Report(int value);
}
```

The raw host must receive JavaScript functions for the exact module name
(`app.host` above). Selecting raw output supplies the compatible ABI; it does
not invent or globally discover that JavaScript module. For a browser page,
declare the module file in the application project:

```xml
<ItemGroup>
  <NetWasmJavaScriptModule Include="page-host.mjs" Module="app.host" />
</ItemGroup>
```

The same item supplies modules to `JsModule` projects and to ordinary raw
browser applications. The SDK bundles the module into the generated browser
bootstrap. Plain names
such as `app.host` work here; this route does not require a versioned application
provider or a copied bootstrap. When the bootstrap runs on the page, the module
runs in that page's realm and can access its DOM:

```js
// page-host.mjs
export function report(value) {
  document.querySelector("#result").textContent = String(value);
}
```

Publish with `NetWasmPublishTarget=browser`, serve the publish directory from
any static web server, and call the generated `run()` from
`./browser/<AssemblyName>.mjs` in the page's module script. During development,
`dotnet run -p:NetWasmPublishTarget=browser` does both; see
[Run a browser project](#run-a-browser-project). It starts the
published deployment with the project's arguments, environment and grants; see
[JavaScript interop](javascript-interop.md) for its options and for
`executeNetWasm`, which takes a request you build yourself. This is an authored
module binding; the host does not read a global module registry. Custom hosts can instead use their declared
application-provider contracts. WASI-backed
APIs supplied by NetWasm, including `HttpClient`, sockets and files, do not
become application-owned `[JSImport]` modules and do not require raw output
for this reason.

Configuration failures identify the mismatched boundary:

- `NW1009` means reached imports are outside the selected WIT world.
- `NW1010` rejects a requested component boundary that cannot represent the
  reached application-owned JavaScript imports.
- `NetWasmComponentContract` is no longer used; a project that still sets it
  fails with a message asking you to remove it.

JavaScript module and worker projects select their boundary with `OutputType`.
Use `dotnet new netwasm-app --host page`, `--host worker` or `--worker wit` for
those workflows. The [C# Web Workers guide](web-workers.md) covers both
boundaries, generated clients, notifications, failure handling, and worker
lifecycle.

An asynchronous `Main` remains alive until its task completes. A JavaScript
event can invoke a delegate registered through `[JSImport]` and complete that
task. When C# should keep answering JavaScript for the life of the page,
publish a `JsModule` and call its exports instead of keeping `Main` waiting. A
service returning `JSSubscription` must return an object
with a `dispose()` method, such as `{ dispose() { /* remove the listener */ } }`.
Release the subscription in a managed `finally` block. Output sinks passed to
`executeNetWasm` must be frozen objects with a synchronous `write(bytes)` method.

Command execution returns an execution outcome, not a live export table; a
`JsModule` provides live exports on the page or in a worker. The
`--host worker` template generates a `*.worker-client.mjs` module:
`await createWorker({ onNotification })` waits for readiness and returns named
Promise-based export methods. Worker code uses notifications to request DOM
updates on the page. `dispose()` drains accepted calls before closing;
`terminate()` stops the worker and rejects pending calls. Calls after either
closure are rejected. This worker boundary does not expose exports from a
page's already-running `Main`.

When a `Task` or `ValueTask` export faults, its rejected promise preserves the
managed message in `error.message`. The worker error's `managed` property also
contains `typeId`, `typeName`, `message` and `stackTrace`. Debug traces include
available source files and line numbers. Optional diagnostic metadata can be
unavailable, in which case `typeName` is null and the numeric type ID remains.

### Write an HTTP service

An HTTP service is a component whose world is NetWasm's `service` world
(`wasi:cli` imports plus `wasi:http/service`). `dotnet new netwasm-serverless`
creates one, with a README showing how to serve it. By hand: select the
`service` world and implement one method that takes an `HttpRequestMessage` and
returns an `HttpResponseMessage`. `System.Net.Http` is part of NetWasm.App, so
the project needs no package for it:

```xml
<Project Sdk="NetWasm.Sdk/<version>">
  <PropertyGroup>
    <OutputType>Component</OutputType>
    <TargetFramework>netwasm0.1</TargetFramework>
    <NetWasmWorld>service</NetWasmWorld>
  </PropertyGroup>
</Project>
```

```csharp
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Greeter;

public static partial class ServiceExports
{
    public static async partial Task<HttpResponseMessage> Handle(HttpRequestMessage request)
    {
        var body = await request.Content!.ReadAsStringAsync();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{request.Method} {request.RequestUri!.PathAndQuery} [{body}]"),
        };
    }
}
```

The class goes in the project's root namespace (set
`NetWasmHttpServiceNamespace` to use another). The package generates the
`wasi:http` handler that turns each request into an `HttpRequestMessage`,
calls `Handle`, and streams the response back with its status, headers and
content. An exception from `Handle` answers with `wasi:http`'s internal error,
which hosts send as a 500. The service can use `HttpClient` and the other
WASI-backed APIs while it handles a request.

`dotnet run` does not serve a service; it says how to publish and host it.
`dotnet publish` writes `Greeter.wasm`, which any `wasi:http` 0.3 host runs
unchanged:

```bash
wasmtime serve -Sp3 -Scli --addr 127.0.0.1:8080 publish/Greeter.wasm
```

Spin runs it from a manifest whose component `source` is the published file
(list the origins `HttpClient` calls in `allowed_outbound_hosts`), and
wasmCloud's `wash dev` from a project configuration whose
`build.component_path` names it. Node.js cannot serve WASI 0.3 HTTP components
yet: jco and its preview3-shim do not serve them correctly.

### Host capabilities

Only reachable APIs retain managed code, Wasm imports and JavaScript providers.
The local launcher supplies clocks, randomness and HTTP when needed; a custom
host can restrict those capabilities.

File access is capability-based: `File`, `FileStream`, `Directory` and the
rest of `System.IO` work on the directories the host mounts into the
application's filesystem sandbox (WASI calls them **preopens**), and nothing
else on the host is reachable.

`dotnet run` and `dotnet test` mount two directories, so ordinary code behaves
as it does on desktop .NET:

- the working directory (the project directory, unless `RunWorkingDirectory`
  says otherwise), read-write, at its own absolute path. It is also the
  program's current directory, so relative paths resolve as usual;
- a private temporary directory, created for the run and deleted after it, at
  `/tmp`, where `Path.GetTempPath()` and `Path.GetTempFileName()` point.

Set `NetWasmGrantWorkingDirectory` or `NetWasmGrantTemporaryDirectory` to
`false` to leave either out. Mount further directories with `NetWasmPreopen`;
this maps the project's `data` directory to `/data`:

```xml
<ItemGroup>
  <NetWasmPreopen Include="$(MSBuildProjectDirectory)/data"
                  GuestPath="/data"
                  Access="readOnly" />
</ItemGroup>
```

`Include` is the host directory, `GuestPath` is its absolute sandbox path, and
`Access` is either `readOnly` or `readWrite`. Guest paths must use canonical
forward-slash syntax and must be unique within the project. `readOnly` blocks
filesystem mutation through that mount; use `readWrite` only when the
application genuinely needs to modify the mapped directory.

### Feature switches

A project sets .NET's feature switches as it would for any .NET application:
with properties such as `<UseSystemResourceKeys>`, `<InvariantGlobalization>` or
`<HttpActivityPropagationSupport>`, or with a `RuntimeHostConfigurationOption`
item. `AppContext` reads them as .NET reads `runtimeconfig.json`. A switch
marked `Trim="true"`, as every one the SDK sets from a property is, is also
folded when the program compiles: the code it turns off leaves the program, as
trimming removes it, and so do the code paths that an assembly's embedded
`ILLink.Substitutions.xml` stubs for it, the framework's included (while
`EventSourceSupport` is false, `EventSource.IsEnabled()` is false and event
logging leaves the program).

NetWasm's defaults include no more than Blazor WebAssembly's:
`UseSystemResourceKeys` is true (NetWasm deploys no manifest resources, so
libraries report resource keys), `Http3Support` and `EnableUnsafeUTF7Encoding`
are false, and `DebuggerSupport` is false outside Debug builds.
`UseSizeOptimizedLinq` follows the optimization preset: false at `O1`, `O2` and
`O3`, which take LINQ's speed-optimized iterators, and true otherwise, including
the Release default `Oz`. Diagnostics are off, as in .NET's own WebAssembly runtime:
`MetricsSupport`, `HttpActivityPropagationSupport` and `EventSourceSupport` are
false, and a project that wants HttpClient's distributed tracing or metrics sets
the first two to true.

### Run a browser project

`dotnet run` runs what `dotnet publish` produces. An application published for
the local launcher, the default, runs in Node. A browser project, a `JsModule`
or `WitWebWorker` project or an application published with
`NetWasmPublishTarget=browser`, is served on `http://localhost:5080/`, or
another free port when 5080 is in use, until you stop it:

```text
$ dotnet run
Now listening on: http://localhost:5080/
```

Open the address to load the page the project publishes, its `index.html`. The
server serves each file the project publishes from where the build left it, at
the path `dotnet publish` copies it to, so it serves the published site without
publishing it. A project that publishes no `index.html`, such as a worker
module whose page is in another project, stops with `NWSDK052`; run the
project with the page instead. Choose the port with `dotnet run -- --port
8080`. The server is for development: it listens on the loopback interface
only and serves each file as it is on disk, without caching. A library cannot
be run (`NWSDK018`).

Under `dotnet watch`, the server stays up for the whole session. Each rebuild
after a save updates the site it serves, and the pages it serves reload
themselves when the build ends.

The site is every file the project publishes: its content items, NetWasm's own
files and its workers' files. A target that copies files into the publish
directory after `Publish` adds them to the published site only, not to the
served one. Declare such files during the build instead, as `NetWasmSiteFile`
items with the `SitePath` they are served at, relative to the publish
directory, in a target listed in `NetWasmCollectSiteFilesDependsOn`:

```xml
<PropertyGroup>
  <NetWasmCollectSiteFilesDependsOn>$(NetWasmCollectSiteFilesDependsOn);AddShellFiles</NetWasmCollectSiteFilesDependsOn>
</PropertyGroup>

<Target Name="AddShellFiles">
  <ItemGroup>
    <_ShellFile Include="shell/**/*" />
    <NetWasmSiteFile Include="@(_ShellFile->'%(FullPath)')"
                     SitePath="%(_ShellFile.RecursiveDir)%(_ShellFile.Filename)%(_ShellFile.Extension)" />
  </ItemGroup>
</Target>
```

Name the metadata through another item, as `_ShellFile` does here: inside a
target, a bare `%(RecursiveDir)` would apply each `NetWasmSiteFile` declared
before it. `dotnet publish` copies each declared file to its `SitePath`. A file
with `Fallback="true"` is served and published only where no other file has its
path.
## 4. Publish

Publish a portable local deployment:

```bash
dotnet publish -c Release -o publish/local
```

The local publish contains the selected `netwasm.host.mjs` launcher and its
portable deployment closure. Copy the folder anywhere and run it with your own
Node.js, as `dotnet run` does:

```bash
node publish/local/netwasm.host.mjs first second
```

The launcher passes the arguments to `Main`, inherits the environment, grants
the network, clocks and random numbers, and exits with the program's exit code.
A published run mounts no directories unless you name them with
`--dir HOST[::GUEST]`, as Wasmtime's `--dir` does, before the program's
arguments (`--` ends the launcher's options). A directory without a guest path
is seen at its own absolute path, and the program starts in the shell's
working directory:

```bash
node publish/local/netwasm.host.mjs --dir . --dir ./data::/data first second
```
 A program that uses `HttpClient` carries the
Node.js HTTP client it needs under `node_modules`, so nothing else is
installed. A command component also runs in any WASI 0.3 host, such as
[Wasmtime](https://wasmtime.dev/):

```bash
wasmtime run --dir . publish/local/HelloNetWasm.wasm first second
```

Add `-Shttp -Sinherit-network` for a program that uses `HttpClient`.

Publish the browser-selected deployment instead:

```bash
dotnet publish -c Release -p:NetWasmPublishTarget=browser -o publish
```

The SDK writes the selected browser deployment to `publish/browser`, alongside
the portable `publish/local` deployment above. Release publishing selects and
minifies only the JavaScript host closure needed by that deployment. It does
not bundle unused local/component or browser/raw strategies into one universal
file. Set `-p:NetWasmMinifyJavaScript=false` only when readable JavaScript is
useful for diagnosis.

A Release page or worker also trusts its deployment manifest instead of
validating its shape again: the build validated it, and the host checks its
SHA-256 against the digest built into the page. Set
`-p:NetWasmValidateDeploymentManifest=true` to keep the check, as Debug builds
do.

## 5. Create a dual-target library

```bash
cd ..
mkdir HelloNetWasm.Library
cd HelloNetWasm.Library
dotnet new netwasm-lib
dotnet restore
dotnet build
dotnet pack -c Release
```

The library targets `netwasm0.1` and `net10.0`. The consuming application
selects its host and capabilities.

## 6. Generate C# bindings from WIT when needed

A `NetWasmWit` item generates C# bindings for a WIT world during the build,
with the project's own NetWasm; the [WIT bindings guide](wit-bindings.md)
describes it. The same generator is also a standalone .NET tool:

```bash
dotnet new tool-manifest
dotnet tool install NetWasm.Wit.Bindings.Tool --version <version>
dotnet netwasm-wit-bindgen --wit service.wit --world example:service@1.0.0/service --output Bindings.g.cs
```

Install the tool at the NetWasm version the project builds with; the local
tool manifest keeps the two together.

Omit `--world` when the WIT document contains exactly one world. The tool
carries the pinned platform-neutral wasm-tools module and does not require a
native `wasm-tools` installation. This standalone command is separate from the
SDK build: it still needs Node.js 24+ available on `PATH`, through
`NETWASM_NODE_PATH` or through `EMSDK_NODE`.

Use `--accessibility internal` for application-private generated types (the
default is `public`). The [WIT bindings guide](wit-bindings.md) covers a
complete example, dependency layout, generated types, and regeneration.
See also the [JavaScript interop guide](javascript-interop.md) and the
[two-way static C interop tutorial](static-native-interop.md).

## 7. Upgrade NetWasm

A project names the NetWasm SDK version it builds with on the first line of its
project file:

```xml
<Project Sdk="NetWasm.Sdk/<version>">
```

To upgrade, change that version to the release you want, then restore and
build:

```bash
dotnet restore
dotnet build
```

The SDK version selects the matching runtime, toolchain and host-tools
packages, so a project references none of them itself. Libraries and test
projects upgrade the same way.

Update the templates, so that new projects start on the latest release:

```bash
dotnet new install NetWasm.Templates
```

If you generate WIT bindings with the standalone tool, update it to the same
version and regenerate; bindings from `NetWasmWit` items regenerate with the
build:

```bash
dotnet tool update NetWasm.Wit.Bindings.Tool --version <version>
```

To keep every project in a repository on one version, leave the version off
each `Sdk` attribute (`<Project Sdk="NetWasm.Sdk">`) and set it once in the
repository's `global.json`:

```json
{
  "msbuild-sdks": {
    "NetWasm.Sdk": "<version>"
  }
}
```

A version on the `Sdk` attribute takes precedence over `global.json`, which
applies only to projects that leave it off. Projects created by earlier
templates use this form; change the version in `global.json` to upgrade them.
