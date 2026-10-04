# NetWasm.Sdk

`NetWasm.Sdk` contains the project-SDK implementation for the current
`netwasm0.1` profile. It composes `Microsoft.NET.Sdk` and maps the user-facing
alias to the tested NetWasm identity.

Executable projects use restored, platform-neutral `NetWasm.Toolchain`,
`NetWasm.Hosting`, and `NetWasm.Hosting.Build` packages. They also restore one
`NetWasm.HostTools.<host RID>` package containing the pinned Node, LLD and
Binaryen executables for the development host. The SDK validates those tools
before building. The platform-neutral `wasm-tools` module is restored through
`NetWasm.Toolchain`; Wasmtime is optional and is not part of the standard build,
run or publish path. The SDK creates a manifest-complete local deployment and
execution request for `dotnet run`; `dotnet publish` copies that exact portable
closure or, with `-p:NetWasmPublishTarget=browser`, emits the browser-selected
closure. Release JavaScript is minified by default.

Plain libraries do not restore host tools. Their runnable consumers select
their own development host package. Host tools never enter the application
deployment or a library's dependency graph.

## Static native requirements

A project can declare prebuilt WebAssembly archives using ordinary items:

```xml
<ItemGroup>
  <NativeLibrary Include="native/wasm32/libexample.a"
                 NetWasmLibraryName="example" WasmTarget="wasm32" />
  <NativeLibrary Include="native/wasm64/libexample.a"
                 NetWasmLibraryName="example" WasmTarget="wasm64" />
</ItemGroup>
```

All three fields are required. `NetWasmLibraryName` matches the logical import
name exactly; it does not need an `.a` suffix. There is no dynamic-library
loading or library-name filesystem search. Only regular `.a` archives are
accepted, with matching WebAssembly target and supported features.

Source libraries forward these requirements through managed `ProjectReference`
edges. Relative paths are resolved in the declaring project, not the consuming
root. Libraries do not compile, hash or copy archives during collection and do
not select host tools or permissions. Applications and test executables use the
same collection. Build-only and disabled reference edges do not contribute
requirements, including requirements from their descendants. A metadata query
still runs when `BuildProjectReferences=false`; it consumes the normal restored
project graph without rebuilding referenced assemblies.

Packages can contribute the same items through ordinary package-relative
`buildTransitive` targets. The executable root selects providers for reached
native calls and its target width. Identical contributions can coalesce;
conflicting providers fail. Unused and other-width archive paths are not opened.
Archive content, not just timestamps, participates in native materialization's
cache identity.

Source-project requirements arrive during target execution. To customize those
items, use a normal target after collection and before the getter:

```xml
<Target Name="CustomizeNativeLibraries" BeforeTargets="NetWasmGetNativeLibraries">
  <ItemGroup>
    <NativeLibrary Remove="@(NativeLibrary)"
                   Condition="'%(NativeLibrary.NetWasmLibraryName)' == 'example'" />
    <NativeLibrary Include="native/custom/libexample.a"
                   NetWasmLibraryName="example" WasmTarget="$(NetWasmTarget)" />
  </ItemGroup>
</Target>
```

An evaluation-time `Remove` cannot remove source requirements that have not
arrived yet. There is no separate override precedence rule.

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
`false` value preserves executable component builds. Raw output is independent
of the entry contract: `async-command` works with component or raw output.
Reachable application-owned `[JSImport]` calls require raw output and an exact
application provider. See the
[entry-contract matrix](../../docs/sdk-quickstart.md#choose-the-entry-contract-and-output-kind).

NetWasm library projects intentionally reject `dotnet run` and
`dotnet publish` for the `netwasm0.1` inner build. Desktop inner builds of a
dual-target library continue to use the stock SDK.

The SDK-owned pack boundary is selected only for projects declaring
`netwasm0.1`. Desktop-only projects continue through the stock NuGet pack
targets.

See the repository's [package-consumer quickstart](../../docs/sdk-quickstart.md)
for supported hosts, restore behavior and application templates.
