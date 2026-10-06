# Size measurements and methodology

## NetWasm 0.7.0: the 55,825-byte Compact result

The reference Release `Console.WriteLine(42)` WASI Preview 2 component is
**55,825 bytes (54.5 KiB)** with Compact, the default since NetWasm 0.7.0.
This includes the linked runtime and precise, non-moving collector; there is
no separate desktop .NET runtime to download. A clean released 0.7.0 SDK
consumer reproduced this size with .NET SDK 10.0.401 on macOS arm64 and printed
`42` under Wasmtime. The package-consumer matrix also checks the exact-size
canary on .NET 10 and 11.

The generated console application's source is:

```csharp
using System;

Console.WriteLine(42);
```

Use the canonical assembly name `NetWasmApp` for exact-size reproduction.
Assembly identity participates in deterministic method/type ordering, so
renaming an otherwise identical assembly can change a few encoded index bytes.
For example, the same released SDK produced 55,827 bytes for `HelloNetWasm`.

The end-user reproduction uses the .NET SDK and restores host tools through
NuGet:

```sh
dotnet new install NetWasm.Templates::0.7.0
dotnet new netwasm-app -n NetWasmApp
cd NetWasmApp
dotnet restore
dotnet publish -c Release -o publish/local
dotnet run -c Release
wc -c publish/local/NetWasmApp.wasm
```

The final component also runs directly with Wasmtime and prints `42`:

```sh
wasmtime run publish/local/NetWasmApp.wasm
```

### Collector selection and compression

| Collector | Uncompressed component | Brotli quality 11 | gzip level 9 |
| --- | ---: | ---: | ---: |
| Compact (default) | 55,825 bytes | 14,215 bytes | 16,776 bytes |
| Boehm (explicit opt-in) | 84,653 bytes | 26,216 bytes | 30,321 bytes |

These are complete optimized components for the same application and command
WIT, not isolated collector sizes. The compression measurements use Brotli's
generic mode with `lgwin=22`. See
[Choosing a garbage collector](runtime-garbage-collection.md) for selection and
tradeoffs. To reproduce the Boehm row with the released SDK, explicitly set
`NetWasmGarbageCollector=Boehm` when publishing; it is not the default.

From a source checkout with the pinned maintainer tools, reproduce both
collectors into a new absolute evidence directory:

```sh
bash eng/measure-console42-size.sh /absolute/new/evidence --compare-collectors
```

The script validates and executes both components and records compressed
sizes and SHA-256 hashes. SDK users do not need the maintainer tool setup.

The repository keeps separate exact-size canaries for Compact and Boehm.
Intentional compiler, runtime, SDK or native-tool changes require source and
clean package-consumer reproduction before updating the corresponding pin.
Optional rich exception diagnostics remain absent from this Release artifact.

The figures exclude the host engine, JavaScript hosting/transpilation files,
deployment manifests and optional sidecars. They are neither whole browser
deployment sizes nor startup, memory-use or performance scores. Exact size
depends on the workload, assembly identity, package version and toolchain.

## Blazor WebAssembly AOT: printing `42`

Measured on 2026-09-30 on macOS arm64, using separate clean projects with
identical source and no inherited bin/obj output. Both apps have an empty root
component. They print `42` and start the ordinary Blazor browser host.

| Build | .NET SDK | Runtime / WebAssembly package | Native runtime Wasm bytes | All loaded Wasm bytes |
| --- | --- | --- | ---: | ---: |
| .NET 10 | 10.0.401 | 10.0.9 | 9,474,625 | 12,276,684 |
| .NET 11 RC1 | 11.0.100-rc.1.26425.128 | 11.0.0-rc.1.26425.128 | 11,726,126 | 15,289,241 |

The .NET 10 project used the already-installed workload 10.0.301.1, pinned
through `sdk.workloadVersion` in its project-local global.json. The .NET 11
project used its matching RC1 workload. Both published in Release with these
explicit settings:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RunAOTCompilation>true</RunAOTCompilation>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>full</TrimMode>
    <InvariantGlobalization>true</InvariantGlobalization>
    <WasmStripILAfterAOT>true</WasmStripILAfterAOT>
    <WasmNativeStrip>true</WasmNativeStrip>
    <WasmNativeDebugSymbols>false</WasmNativeDebugSymbols>
    <EmccCompileOptimizationFlag>-Oz</EmccCompileOptimizationFlag>
    <EmccLinkOptimizationFlag>-Oz</EmccLinkOptimizationFlag>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly"
                      Version="10.0.9" />
  </ItemGroup>
</Project>
```

Name the project `Blazor42.csproj`. For the .NET 11 reproduction, change the
TFM to `net11.0` and the package version to `11.0.0-rc.1.26425.128`.
Select the SDK versions above in separate global.json files. The .NET 10
file used:

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "disable",
    "workloadVersion": "10.0.301.1"
  }
}
```

For .NET 11, select `11.0.100-rc.1.26425.128` with `rollForward: disable`
and omit `workloadVersion`. Install the matching `wasm-tools` workload if it
is not already available.

`Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Blazor42;

Console.WriteLine(42);
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
await builder.Build().RunAsync();
```

`App.razor` contains only a Razor comment, so it renders no UI:

```razor
@* Empty root: this app only writes 42 to the console. *@
```

`wwwroot/index.html`:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Blazor AOT 42</title>
  <base href="/">
  <link rel="preload" id="webassembly">
  <script type="importmap"></script>
</head>
<body>
  <div id="app">Loading</div>
  <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
</body>
</html>
```

Publish each clean project:

```sh
dotnet publish Blazor42.csproj -c Release -o publish --source https://api.nuget.org/v3/index.json
```

Sum the raw `*.wasm` files under `publish/wwwroot/_framework`, excluding
`.br` and `.gz` copies. The .NET 10 build contains 32 Wasm files; .NET 11
contains 36. Chromium verification confirmed that all of those files were
loaded, their uncompressed response lengths matched the published totals,
`42` appeared in the console, the empty component rendered, and no browser
errors occurred.

Relative to the 55,825-byte NetWasm Compact component, the measured total Wasm
payloads are 219.91x and 273.88x as large. Counting only the native runtime
module gives 169.72x and 210.05x. MB means 1,000,000 bytes; KiB means 1,024
bytes. Neither total includes JavaScript, HTML or the host engine.

This is [Mono WebAssembly AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0),
with [IL stripping](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0),
rather than CoreCLR Native AOT. The Blazor outputs use a JavaScript browser
host; NetWasm produces a WASI Preview 2 component. These measurements describe
the tested app and settings, not a minimum size for every .NET Wasm app or a
comparison of equivalent UI frameworks.

## Rust: a footprint reference, not a GC comparison

The [Rust target documentation](https://doc.rust-lang.org/rustc/platform-support/wasm32-wasip2.html)
confirms that `wasm32-wasip2` produces a Component rather than a raw core
module. The local runs below therefore compare uncompressed final components,
not a Rust core module against a wrapped C# program.

For the numeric-output workload, the Rust source is:

```rust
fn main() {
    println!("{}", 42);
}
```

| Toolchain and build | Final component |
| --- | ---: |
| NetWasm 0.7.0, ordinary Release, Compact | 55,825 bytes |
| Rust 1.90.0, ordinary Cargo release | 86,248 bytes |
| Rust 1.95.0, ordinary Cargo release | 81,997 bytes |
| Rust 1.95.0, size-oriented profile below | 53,635 bytes |

All four artifacts executed and printed `42` under Wasmtime. The retained
Rust runs used Wasmtime 47.0.3; the NetWasm row uses the current Compact result.
The Rust artifacts also pass wasm-tools 1.256.0 validation.

This is a deliberately small console-output workload, not a language-wide
ranking or a claim that NetWasm matches the smallest possible Rust binary.
Rust does not bundle a tracing GC here; NetWasm does. The different runtime
features, standard-library coverage and optimization defaults matter.

### Reproduce the Rust runs

Install the pinned Rust toolchains without changing your default:

```sh
rustup toolchain install 1.90.0 --profile minimal --target wasm32-wasip2
rustup toolchain install 1.95.0 --profile minimal --target wasm32-wasip2
```

Create a Cargo binary project with the source above and this `Cargo.toml`:

```toml
[package]
name = "console42"
version = "0.0.0"
edition = "2021"

[profile.small]
inherits = "release"
opt-level = "z"
lto = true
codegen-units = 1
panic = "abort"
strip = "symbols"
```

Build and measure separate output directories:

```sh
cargo +1.90.0 build --release --target wasm32-wasip2 --target-dir target-190
cargo +1.95.0 build --release --target wasm32-wasip2 --target-dir target-195
cargo +1.95.0 build --profile small --target wasm32-wasip2 --target-dir target-195
wc -c target-190/wasm32-wasip2/release/console42.wasm
wc -c target-195/wasm32-wasip2/release/console42.wasm
wc -c target-195/wasm32-wasip2/small/console42.wasm
```

See [Cargo's profile documentation](https://doc.rust-lang.org/cargo/reference/profiles.html)
for optimization, LTO, panic and stripping semantics. These runs use the
toolchains' distributed standard libraries, not a custom size-optimized
standard-library rebuild.

### What the 2025 table actually establishes

[RIoT Secure's October 2025 experiment](https://www.riotsecure.com/blog/wasm_binary_size_in_high_Level_languages)
reports 86,254 bytes for Rust 1.90.0 `wasm32-wasip2` Hello World and
110,084 bytes for TinyGo 0.39.0. A local Rust 1.90.0 project named
`hello_world`, with `println!("Hello World")` and ordinary Cargo release,
produces 86,269 bytes and executes successfully. That confirms the reported
scale, not the exact published byte count.

The article does not supply a complete Cargo project or artifact hashes, and
some printed code snippets contain syntax errors. Its other rows also mix
Preview 1 core modules with the Rust Preview 2 component. Do not present that
table as a fully reproduced, equivalent-target ranking.

## Managed-language footprint comparison

Kotlin is the closer managed-language reference: both programs print `42`
through WASI. TinyGo's published Hello World result provides additional context.
These are **not equivalent deployment formats, collector boundaries or build
settings**:

| Build | Artifact | Bytes | Collector boundary |
| --- | --- | ---: | --- |
| NetWasm 0.7.0 Release, Compact | WASI Preview 2 Component | 55,825 | Precise Compact GC included in the artifact |
| Kotlin 2.4.0 production `wasmWasi` | WASI Preview 1 core module | 79,293 | WasmGC supplied by the host engine, not the artifact |
| TinyGo 0.39.0 Hello World* | WASI Preview 1 core module | 110,084 | Precise GC configured |

\* Source: [RIoT Secure, October 2025](https://www.riotsecure.com/blog/wasm_binary_size_in_high_Level_languages),
using `-target=wasi -opt=2 -no-debug`. TinyGo 0.39.0's
[`wasi` alias](https://github.com/tinygo-org/tinygo/blob/v0.39.0/targets/wasi.json)
selects [`wasip1`, with precise GC configured](https://github.com/tinygo-org/tinygo/blob/v0.39.0/targets/wasip1.json).

The NetWasm and Kotlin artifacts execute and print `42`; the Kotlin module
passes wasm-tools validation. These are console-output footprint tests, not
allocation stress tests or demonstrations of a collection cycle. Runtime
configuration does not mean that every possible runtime operation survives
reachability trimming.

### Where the collector lives

NetWasm bundles its collector into the application Wasm and uses typed heap
descriptors and exact compiler-maintained roots. Kotlin/Wasm uses the engine's
WasmGC implementation instead: the 79,293-byte module does not contain a
standalone collector. Neither figure includes the host engine.

The component sizes above include reachable runtime and application code as
well as the collector. They do not establish an isolated GC size.

### Kotlin

The source is `fun main() { println(42) }`, compiled directly to Wasm using
JetBrains' official Kotlin/Wasm compiler, not JVM bytecode. The Gradle 9.5.1
project uses OpenJDK 26.0.1 for build tooling, Kotlin Multiplatform plugin
2.4.0, Maven Central and
`wasmWasi { nodejs(); binaries.executable() }`:

No JVM is required to run the resulting artifact.

```sh
gradle --no-daemon compileProductionExecutableKotlinWasmWasi
wasmtime run build/compileSync/wasmWasi/main/productionExecutable/kotlin/console42.wasm
```

The root project is named `console42`, with the source in
`src/wasmWasiMain/kotlin/Main.kt`. The official
[Kotlin WASI guide](https://kotlinlang.org/docs/wasm-wasi.html) documents Preview
1 for this target, while [Kotlin's configuration reference](https://kotlinlang.org/docs/wasm-configuration.html)
describes its engine WasmGC requirement. The collector is not bundled into the
79,293-byte module. No Preview 2 adapter or component envelope was added.

## JSON is not one fixed cost

Earlier isolated Release/wasm32 **core-module** measurements of the selected
System.Text.Json workloads produced:

| Workload | Whole core module |
| --- | ---: |
| No JSON, matched baseline | 54,230 bytes |
| JsonDocument parsing | 670,270 bytes |
| Source-generated serialization only | 1,328,195 bytes |
| Typed source-generated deserialization only | 2,741,193 bytes |
| Serialization and typed deserialization | 3,475,533 bytes |

These are historical scenario measurements, not current component sizes,
not incremental package costs, and not minimum sizes for arbitrary JSON
programs. Do not compare their 54,230-byte baseline directly with the current
55,825-byte Compact component. `JsonDocument` is also not the mutable `JsonNode` API.

The useful result is granularity: choosing a different JSON workload retains
a different closure. Adding a package reference alone is not the same thing
as exercising every implementation in it.

## Optional data and diagnostics

Timezone data is an explicit deployment sidecar rather than an embedded
database in each application Wasm. UTC needs no timezone database; selected
local-time behavior requires the matching asset and host capabilities. See
[timezone assets](timezone-assets.md).

Debug builds enable managed stack-trace instrumentation and its symbol
sidecar. Release omits both unless requested with
`NetWasmManagedStackTrace=true`. This does not add general reflection or a
runtime-discoverable type-name catalogue. See the
[SDK quickstart](sdk-quickstart.md) and
[managed stack-trace contract](support-status.md#managed-stack-traces).
