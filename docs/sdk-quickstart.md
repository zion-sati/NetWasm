# NetWasm SDK quickstart

This is the ordinary package-consumer path for creating, building, running and
publishing a NetWasm application. It does not require a NetWasm checkout or a
separate LLVM, Node.js, `wasm-tools` or Wasmtime installation.

The commands below describe the production NuGet.org experience. Normal NuGet
configuration and credentials apply; NetWasm does not define a product-specific
package source.

Before porting an application, review the
[first-preview WASI-backed .NET surface](support-status.md#first-preview-wasi-backed-net-surface).
The first preview has no general managed file/directory API, raw socket API,
subprocess API, or managed threading/thread-pool runtime. Its in-memory streams,
selected WASI HTTP and CLI contracts, async reactor, and read-only timezone
mount are deliberately narrower capabilities.

## 1. Install the prerequisites

Install [.NET SDK 10.0.300 or newer](https://dotnet.microsoft.com/download/dotnet/10.0),
Git and Python 3, then install the pinned Emscripten SDK. Python is used by the
`emsdk` installer; it is not a NetWasm runtime dependency. Emscripten supplies
the Node.js and LLVM/LLD versions used by the NetWasm SDK. The supported
minimums are Node.js 24+ and LLD 24+.

### macOS and Linux

The `emsdk` downloader uses standard `tar`/xz extraction support. Minimal
Debian/Ubuntu images may need `apt-get install git python3 xz-utils` first;
normal developer installations commonly already provide these tools.

```bash
git clone https://github.com/emscripten-core/emsdk.git ~/emsdk
cd ~/emsdk
./emsdk install 6.0.7
./emsdk activate 6.0.7
source ~/emsdk/emsdk_env.sh
```

Run the `source` command in each new shell before using NetWasm. It sets
`EMSDK` and `EMSDK_NODE`; NetWasm uses that activated SDK selection without
requiring the Node directory on `PATH`. NetWasm resolves `wasm-ld` from
`$EMSDK/upstream/bin`, so no extra `PATH` edit is needed.

### Windows PowerShell

On Windows ARM64, Emscripten 6.0.7's release toolchain is x64. Select that
toolchain explicitly before installation; Windows runs it through its normal
x64 emulation. Omit the first command on Windows x64.

```powershell
$env:EMSDK_ARCH = 'x86_64' # Windows ARM64 only
git clone https://github.com/emscripten-core/emsdk.git "$env:USERPROFILE\emsdk"
Set-Location "$env:USERPROFILE\emsdk"
Set-ExecutionPolicy -Scope Process Bypass
.\emsdk.ps1 install 6.0.7
.\emsdk.ps1 activate 6.0.7
. .\emsdk_env.ps1
```

The execution-policy change applies only to this PowerShell process; it does
not alter the user or machine policy. Dot-source `emsdk_env.ps1` in each new
PowerShell session before using NetWasm. It updates that shell's environment
without defining a permanent NetWasm-specific tool path.

Verify the active shell:

```bash
dotnet --version
emcc --version
"$EMSDK_NODE" --version
"$EMSDK/upstream/bin/wasm-ld" --version
```

In PowerShell, use:

```powershell
dotnet --version
emcc --version
& "$env:EMSDK_NODE" --version
& "$env:EMSDK\upstream\bin\wasm-ld.exe" --version
```

The expected tool families are .NET SDK 10.0.3xx or a newer .NET 10 feature
band, Emscripten 6.0.7, Node.js 24 or newer, and LLD 24 or newer. The
platform-neutral `wasm-tools` module
is carried and verified by `NetWasm.Toolchain`; Wasmtime is optional and is not
needed for the standard SDK build, run or publish journey.

## 2. Install the templates

Install the templates directly from NuGet.org:

```bash
dotnet new install NetWasm.Templates@0.1.0-preview.64
```

The normal NuGet configuration and credentials on the machine continue to
apply. NetWasm does not require a product-specific package source.

## 3. Create and run an application

```bash
mkdir HelloNetWasm
cd HelloNetWasm
dotnet new netwasm-app
dotnet restore
dotnet build
dotnet run
```

The template prints `42`. Its managed entry point is ordinary C# `Main`; the
JavaScript host's `executeNetWasm(...)` function is the reusable execution
facade used by the generated launcher, not a replacement managed entry point.

### Grant optional host capabilities

Applications are deny-by-default. Grant only capabilities required by reachable
code using ordinary MSBuild properties and items:

```xml
<PropertyGroup>
  <NetWasmNetworkPolicy>allowAll</NetWasmNetworkPolicy>
  <NetWasmRandomness>true</NetWasmRandomness>
</PropertyGroup>

<ItemGroup>
  <NetWasmClockGrant Include="monotonic" />
  <NetWasmEnvironmentVariable Include="APP_MODE" Value="preview" />
  <NetWasmPreopen Include="$(MSBuildProjectDirectory)/data"
                  GuestPath="/data"
                  Access="readOnly" />
</ItemGroup>
```

The defaults are `denyAll`, `false`, and empty item collections. Use `wall` or
`monotonic` for `NetWasmClockGrant`; use `readOnly` or `readWrite` for preopen
access. Supplying an environment item also grants that exact name. A retained
capability without its grant is rejected before `Main`, while a grant for an
unreachable capability does not retain the corresponding managed code or Wasm
import. `DateTime.UtcNow` needs `wall`; delay/readiness and generated-regex
timeout checks need `monotonic`; WASI HTTP needs both `allowAll` and `monotonic`; and
`Guid.NewGuid()` needs randomness. A preopen does not create the currently
missing general `File`/`Directory` API surface.

## 4. Publish

Publish a portable local deployment:

```bash
dotnet publish -c Release -o publish/local
```

The local publish contains the selected `netwasm.host.mjs` launcher and its
portable deployment closure. `dotnet run` owns the descriptor/request/result
channels for normal development execution; direct launcher integrations use
the explicit channel contract documented by `NetWasm.Hosting`.

Publish the browser-selected deployment instead:

```bash
dotnet publish -c Release \
  -p:NetWasmPublishTarget=browser \
  -o publish
```

The SDK writes the selected browser deployment to `publish/browser`, alongside
the portable `publish/local` deployment above. Release publishing selects and
minifies only the JavaScript host closure needed by that deployment. It does
not bundle unused local/component or browser/raw strategies into one universal
file. Set `-p:NetWasmMinifyJavaScript=false` only when readable JavaScript is
useful for diagnosis.

## 5. Create a dual-target library

```bash
mkdir HelloNetWasm.Library
cd HelloNetWasm.Library
dotnet new netwasm-lib
dotnet restore
dotnet build
dotnet pack -c Release
```

The library is an ordinary NuGet library targeting `netwasm0.1` and `net10.0`.
It does not select a host or gain capabilities; the consuming application owns
those deployment choices.
