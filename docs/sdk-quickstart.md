# NetWasm SDK quickstart

Before porting an application, review the
[first-preview WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).
The first preview has no general managed file/directory API, raw socket API,
subprocess API, or managed threading/thread-pool runtime. Its in-memory streams,
selected WASI HTTP and CLI contracts, async reactor, and read-only timezone
mount are deliberately narrower capabilities.

## 1. Install .NET

Install [.NET SDK 10.0.300 or newer](https://dotnet.microsoft.com/download/dotnet/10.0).
The supported development hosts are Linux x64/ARM64, macOS ARM64 and Windows
x64/ARM64. On Windows ARM64, use the native ARM64 .NET SDK; the x64 SDK under
emulation cannot select the ARM64 host-tools package.
The pinned Linux tools need glibc 2.28 or newer; the Linux package carries
Node's `libatomic.so.1` dependency. The pinned macOS Node binary needs macOS
13.5 or newer.

The default templates stay on .NET 10. To compile C# 15 source, select the
.NET 11 SDK version recorded in [`eng/csharp15-toolchain.json`](../eng/csharp15-toolchain.json)
in the project's `global.json` and allow prerelease SDKs while .NET 11 is a
prerelease. An isolated .NET 11 SDK installation is sufficient; NetWasm does
not also require the .NET 10 SDK. The target framework remains `netwasm0.1`.

SDK selection does not change NetWasm's `float`/`double` to `decimal`
rounding rule. Even with the .NET 10 SDK and C# 14 or earlier, NetWasm selects
the [newer exact-binary-input conversion](corelib-runtime.md#binary-floating-point-to-decimal-policy),
not desktop .NET 10's seven/fifteen-significant-digit rule. The linked
compatibility note describes the qualified source-branch behavior; check the
release containing this change before relying on it in a published SDK.

For an ordinary application or test project, no separate Emscripten, Node.js,
LLVM/LLD, Binaryen, Git or Python installation is needed. `dotnet restore`
selects one host-tools NuGet package for the machine doing the build. That
package supplies the pinned Node, `wasm-ld`, `wasm-merge` and `wasm-opt` tools.
The first restore downloads the package (about 79 MB compressed on macOS
ARM64, plus the other NetWasm dependencies); NuGet caches it for subsequent
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
Release omits their instrumentation and symbol sidecar unless
`NetWasmManagedStackTrace=true`.

An application or runnable test project restores one host-tools package. A
plain or dual-target library does not; its consumer selects the host package.
If you move a restored checkout to another development host, run `dotnet
restore` again. An ambient `node`, `wasm-ld` or Binaryen installation does not
replace the restored defaults. For an intentional override, set
`NETWASM_NODE_PATH`, `NETWASM_WASM_LD_PATH`, `NETWASM_WASM_MERGE_PATH` or
`NETWASM_WASM_OPT_PATH` to the corresponding executable's absolute path.
Invalid overrides fail validation rather than falling back silently.

If you enable NuGet lock files with `RestorePackagesWithLockFile=true`, the SDK
uses a host-qualified name such as `packages.linux-x64.lock.json` for an app or
test project. Commit the files for the development hosts you support. A
checkout with an older `packages.lock.json` can run an unlocked restore to
generate the new host file, then review and remove the old file. To keep a
deliberate custom name, set `NuGetLockFilePath` explicitly; one explicit lock
file shared across different hosts must be regenerated for each host.

### Host capabilities

Only reachable APIs retain managed code, Wasm imports and JavaScript providers.
The local launcher supplies clocks, randomness and HTTP when needed; a custom
host can restrict those capabilities.

Filesystem access requires explicit host-to-guest mappings. A **preopen** is
WASI's equivalent of mounting a host directory into the application's filesystem
sandbox before the program starts. The application receives access to the guest
path, not ambient access
to the original host path or the rest of the host filesystem.

For example, this maps the project's `data` directory to `/data` inside the
Wasm application:

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

NetWasm does not infer a preopen from the project directory or current working
directory. There is no portable, unambiguous host-to-guest path mapping, and an
implicit mount would expose host files that the application did not request.
With no `NetWasmPreopen` items, no consumer directory is mounted.

A preopen grants the host-side filesystem capability; it does not create the
currently missing general `System.IO.File`, `FileStream` or `Directory` API
surface. Those APIs remain outside the first-preview profile.

## 4. Publish

Publish a portable local deployment:

```bash
dotnet publish -c Release -o publish/local
```

The local publish contains the selected `netwasm.host.mjs` launcher and its
portable deployment closure.

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

WIT binding generation is an optional, separate .NET tool rather than a
compiler command:

```bash
dotnet tool install --global NetWasm.Wit.Bindings.Tool
netwasm-wit-bindgen --wit service.wit --world example:service@1.0.0/service --output Bindings.g.cs
```

Omit `--world` when the WIT document contains exactly one world. The tool
carries the pinned platform-neutral wasm-tools module and does not require a
native `wasm-tools` installation. This standalone command is separate from the
SDK build: it still needs Node.js 24+ available on `PATH`, through
`NETWASM_NODE_PATH` or through `EMSDK_NODE`.
