# NetWasm.Sdk

`NetWasm.Sdk` contains the project-SDK implementation for the experimental
`netwasm0.1` profile. It composes `Microsoft.NET.Sdk` and maps the user-facing
alias to the tested NetWasm identity.

Executable projects use restored, platform-neutral `NetWasm.Toolchain`,
`NetWasm.Hosting`, and `NetWasm.Hosting.Build` packages. The SDK validates the
Node 24 or newer and wasm-ld/LLD 24 or newer supplied by an activated
Emscripten 6.0.7 SDK before
building. The platform-neutral `wasm-tools` module is restored through
`NetWasm.Toolchain`; Wasmtime is optional and is not part of the standard build,
run or publish path. The SDK creates a manifest-complete local deployment and
execution request for `dotnet run`; `dotnet publish` copies that exact portable
closure or, with `-p:NetWasmPublishTarget=browser`, emits the browser-selected
closure. Release JavaScript is minified by default.

Host tools come from the activated Emscripten SDK.

The compiler/runtime layer supplies componentization through the
`NetWasmComponentizeDependsOn` target seam. It receives the core module, WIT,
interop, and runtime inputs through `@(NetWasmComponentInput)` and
`@(NetWasmComponentRuntime)`, and must produce both `NetWasmOutputPath` and
`NetWasmComponentManifestPath`. A missing provider is a loud build error.
This keeps compiler and runtime policy out of the SDK while allowing a
runtime-tier package to plug in without changing project files.

Raw module qualification can set `-p:NetWasmRawWasm=true`. This emits the
managed core module and materialized runtime module, but skips SDK-owned
componentization so an external raw-module linker can consume the standard
`NetWasmComponentInput` and `NetWasmComponentRuntime` contract. The default
`false` value preserves executable component builds.

NetWasm library projects intentionally reject `dotnet run` and
`dotnet publish` for the `netwasm0.1` inner build. Desktop inner builds of a
dual-target library continue to use the stock SDK.

The SDK-owned pack boundary is selected only for projects declaring
`netwasm0.1`. Desktop-only projects continue through the stock NuGet pack
targets.

See the repository's [package-consumer quickstart](../../docs/sdk-quickstart.md)
for the pinned Emscripten activation and application templates.
