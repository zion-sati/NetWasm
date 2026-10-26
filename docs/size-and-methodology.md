# Size measurements and methodology

## C# with precise GC beats Rust's size profile

The measured Release/Oz `Console.WriteLine(42)` WASI Preview 2 component is
**38,919 bytes (38.0 KiB)** with the default precise Compact collector, or
**13,213 bytes with Brotli quality 11**. The same print-`42` workload with
Rust 1.95.0's size-oriented profile produces a **53,635-byte** WASI Preview 2
component, or **16,963 bytes with Brotli quality 11**. NetWasm is **14,716 bytes
(27.4%) smaller raw** and **3,750 bytes (22.1%) smaller compressed**, including
its linked runtime and reachable precise-GC support.

These NetWasm figures were reproduced using the released 0.10.3 SDK and template
packages from NuGet.org. All three collector variants execute and print `42`.
The retained Rust reference uses
the exact toolchain and Cargo profile documented below; it is not a claim about
the smallest possible Rust binary.

### Workload and reproduction

```csharp
using System;

Console.WriteLine(42);
```

Use the canonical assembly name `NetWasmApp` for exact-size reproduction.
Assembly identity participates in deterministic method/type ordering, so
renaming an otherwise identical assembly can change encoded index bytes.
The released-package reproduction ran on macOS arm64 under Wasmtime 49.0.3.

The ordinary released-SDK reproduction is:

```sh
dotnet new install NetWasm.Templates::0.10.3
dotnet new netwasm-app -n NetWasmApp
cd NetWasmApp
dotnet restore
dotnet publish -c Release -p:NetWasmOptimization=Oz -o publish/local
dotnet run -c Release -p:NetWasmOptimization=Oz
wc -c publish/local/NetWasmApp.wasm
wasmtime run publish/local/NetWasmApp.wasm
```

The application prints `42`. The SDK restores its host tools through NuGet;
ordinary SDK users do not need a maintainer Emscripten installation.

### Collector selection and compression

| Collector | Uncompressed component | Brotli quality 11 |
| --- | ---: | ---: |
| Compact (default) | 38,919 bytes | 13,213 bytes |
| NetWasmGC | 41,879 bytes | 14,533 bytes |
| Bump | 34,886 bytes | 11,506 bytes |

These are complete optimized components for the same application and command
WIT, not isolated collector sizes. Brotli uses generic mode with `lgwin=22`.
All three components execute and print `42`; default, explicit Compact and a
switch back to Compact produce identical final bytes. See
[Choosing a garbage collector](runtime-garbage-collection.md) for tradeoffs and
the `NetWasmGarbageCollector` setting.

From a source checkout with the pinned maintainer tools:

```sh
bash eng/measure-console42-size.sh /absolute/new/evidence --compare-collectors
```

The maintainer script validates and executes its collector comparison and
records sizes and hashes. Ordinary consumers use the SDK commands above.
Optional rich exception diagnostics are absent from the Release artifact.

The figures exclude the host engine, JavaScript hosting/transpilation files,
deployment manifests and optional sidecars. They are neither whole-browser
deployment sizes nor startup, memory-use or performance scores. Exact size
depends on workload, assembly identity, package version and toolchain.

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

Relative to the 38,919-byte NetWasm component, these retained
Blazor Wasm payloads are about 315x and 393x as large. The Blazor builds were
measured on the date above. KiB means 1,024 bytes; MiB means 1,048,576 bytes.
Neither total includes JavaScript, HTML or the host engine.

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
| NetWasm, Release/Oz, Compact | 38,919 bytes |
| Rust 1.90.0, ordinary Cargo release | 86,248 bytes |
| Rust 1.95.0, ordinary Cargo release | 81,997 bytes |
| Rust 1.95.0, size-oriented profile below | 53,635 bytes |

All four artifacts executed and printed `42` under Wasmtime. The retained
Rust runs used Wasmtime 47.0.3; NetWasm used Wasmtime 49.0.3.
These are artifact-size observations, not a cross-engine performance comparison.
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

## Broader Wasm footprint comparison

Kotlin and TinyGo provide broader language/runtime context. All four locally
reproduced programs print `42` through WASI and use size-oriented builds. They
are **not equivalent deployment formats or collector boundaries**:

| Build | Artifact | Raw bytes | Brotli 11 | Collector boundary |
| --- | --- | ---: | ---: | --- |
| NetWasm Release/Oz, Compact | WASI Preview 2 Component | 38,919 | 13,213 | Precise Compact GC included in the artifact |
| Rust 1.95.0 size profile | WASI Preview 2 Component | 53,635 | 16,963 | No tracing GC in this workload |
| Kotlin 2.4.0 production `wasmWasi` | WASI Preview 1 core module | 79,293 | 22,655 | WasmGC supplied by the host engine, not the artifact |
| TinyGo 0.39.0 `fmt.Println(42)` | WASI Preview 1 core module | 93,043 | 26,931 | Precise GC configured |

Brotli uses quality 11, generic mode and `lgwin=22` for every artifact.
TinyGo uses `-target=wasi -opt=2 -no-debug`; its
[`wasi` alias](https://github.com/tinygo-org/tinygo/blob/v0.39.0/targets/wasi.json)
selects [`wasip1`, with precise GC configured](https://github.com/tinygo-org/tinygo/blob/v0.39.0/targets/wasip1.json).

All four artifacts execute and print `42`; each passes `wasm-tools validate`.
These are console-output footprint tests, not allocation stress tests or
demonstrations of a collection cycle. Runtime configuration does not mean that
every possible runtime operation survives reachability trimming.

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

### TinyGo

The source is:

```go
package main

import "fmt"

func main() {
    fmt.Println(42)
}
```

Build with the official TinyGo 0.39.0 toolchain:

```sh
tinygo build -target=wasi -opt=2 -no-debug -o console42.wasm main.go
wasmtime run console42.wasm
```

The retained run used the official `tinygo/tinygo:0.39.0` image, whose manifest
digest was `sha256:0e51d243c1b84ec650f2dcd1cce3a09bb09730e1134771aeace2240ade4b32f5`.
No Preview 2 adapter or component envelope was added.

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
38,919-byte Compact component. `JsonDocument` is also not the mutable `JsonNode` API.

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
