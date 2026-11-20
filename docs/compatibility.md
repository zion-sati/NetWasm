# Compatibility and versioning

What NetWasm needs to build a project, where its output runs, and what a new
NetWasm version may change.

## Building

| Requirement | Supported |
| --- | --- |
| .NET SDK | 10.0.303 or newer, including .NET 11. Linux distributions' own `dotnet` packages carry the 10.0.1xx SDK, which is too old; install Microsoft's SDK instead. An older SDK stops with `NWSDK086`. |
| Linux | x64 and ARM64, glibc 2.28 or newer. Musl-based distributions such as Alpine are not supported (`NWSDK041`). |
| macOS | ARM64, macOS 13.5 or newer. |
| Windows | x64 and ARM64. On ARM64, use the native ARM64 .NET SDK. |
| C# | The SDK's language version. C# 15 needs the .NET 11 SDK. |

Nothing else is installed: the first `dotnet restore` downloads the compiler,
Node.js, the linker, Binaryen and wasm-tools for the machine as one NuGet
package. See [install .NET](sdk-quickstart.md#1-install-net).

## Running

| Host | Supported |
| --- | --- |
| Chrome and Edge | 137 or newer |
| Firefox | 131 or newer; 134 for `wasm64` |
| Safari on macOS and iOS | 18.4 or newer; no `wasm64` |
| Node.js | 24.15 or newer. NetWasm's launcher brings its own; this applies when you host raw output in your own Node.js. |
| Wasmtime | 50.0.1, the version NetWasm's tests run on, with WASI 0.3 |
| Other WASI 0.3 hosts | Any host that implements the interfaces a component imports. The [HTTP service](http-services.md) template documents Spin and wasmCloud. |

The browser versions are those that added WebAssembly exception handling, the
newest feature NetWasm output uses. In an older browser, `open()` and `run()`
fail with a `NetWasmUnsupportedBrowserError` that names what is missing. See
[targets and output formats](targets-and-outputs.md) for the full list of
WebAssembly features.

## Packages

A `net10.0-netwasm` project restores a package's `net10.0`, `net9.0`, `net8.0`
or .NET Standard build, newest first, as NuGet does for any `net10.0` project.
The code the program reaches must fit NetWasm's profile: most of the base class
library, without runtime reflection, `dynamic`, runtime assembly loading or
threads. See [packages and libraries](netstandard-libraries.md) and how the
[most popular packages](package-compatibility.md) fare.

A package that ships only `netwasm0.1` assets cannot be used from a
`net10.0-netwasm` project; it needs a `net10.0-netwasm`, or `net8.0` or later,
build.

## Versions

A project names its NetWasm version once, on its `Sdk` attribute or in
`global.json`, and that version selects the matching runtime, toolchain and
host tools. Every NetWasm package of a release carries the same version.
[Upgrade NetWasm](sdk-quickstart.md#7-upgrade-netwasm) shows how to move to a
new one.

NetWasm is pre-1.0. A minor release can change the supported CIL and APIs, the
runtime ABI, generated manifests and build output, and its release notes list
breaking changes. Each minor release comes with matching NetWasm.Libraries and
TUnit-NetWasm packages; upgrade them with the SDK.

`net10.0-netwasm`'s platform version, 1.0, changes only when NetWasm's platform
contract breaks. The older `netwasm0.1` target framework still builds, with
warning `NWSDK105`, and will be removed before 1.0.

From 1.0, the platform version, runtime ABI, host capability contracts, package
compatibility rules and diagnostics follow an explicit compatibility policy;
see the [roadmap](roadmap.md).

There is no supported-version, backport or security-fix policy yet; see
[security](security.md).
