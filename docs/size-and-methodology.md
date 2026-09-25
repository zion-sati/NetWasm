# Size measurements and methodology

## The 89,550-byte C# result

The reference result was updated on 2026-09-25 from clean source commit
`87c9e44ee` with .NET SDK 10.0.302 and the pinned Emscripten 6.0.7 toolchain.
The source-bound canary validated the component, ran it under Wasmtime, printed
`42`, and produced SHA-256
`a15b7e95c04b6533907f67c5e1e4a60e80e4ffdd5a69c79c0cc50ad78fd65ed8`.
This current-source result has not yet been published as a package release.

The generated console application's source is:

```csharp
using System;

Console.WriteLine(42);
```

After the corresponding packages are released, the end-user reproduction uses
only the .NET SDK and restores the host tools through NuGet:

```sh
dotnet new install NetWasm.Templates
mkdir Hello42
cd Hello42
dotnet new netwasm-app
dotnet restore
dotnet publish -c Release -o publish/local
dotnet run -c Release
wc -c publish/local/Hello42.wasm
```

The reference final WASI Preview 2 Component is **89,550 bytes**,
uncompressed. Runtime support, allocation and precise BDWGC/BoehmGC garbage
collection are linked into the program; there is no separate desktop .NET
runtime to download.

The component also runs directly with Wasmtime 47.0.3 and prints `42`:

```sh
wasmtime run publish/local/Hello42.wasm
```

Exact size can vary with the SDK, host and pinned native-tool package. Record
those inputs and the artifact hash when comparing exact bytes.

The figure excludes the host engine, JavaScript hosting/transpilation files,
deployment manifests and optional sidecars. Those files are part of a
JavaScript-host deployment, but not part of this component. It is neither the
whole deployment's byte count nor a startup, memory-use or performance score.

Exact size depends on the workload, compiler and toolchain. The package
version, host and recorded hash identify a specific reproduction.

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
| NetWasm, ordinary Release | 89,550 bytes |
| Rust 1.90.0, ordinary Cargo release | 86,248 bytes |
| Rust 1.95.0, ordinary Cargo release | 81,997 bytes |
| Rust 1.95.0, size-oriented profile below | 53,635 bytes |

All four artifacts executed and printed `42` under Wasmtime 47.0.3.
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
| NetWasm Release | WASI Preview 2 Component | 89,550 | Precise BDWGC/BoehmGC included in the artifact |
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

Our early spike put the bundled BDWGC collector footprint at roughly **30 KB**.

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
89,550-byte component. `JsonDocument` is also not the mutable `JsonNode` API.

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
