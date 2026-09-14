# CLI, build, and deployment entry points

The ordinary consumer workflow uses the published `NetWasm.Sdk` and templates
from NuGet.org with standard `dotnet build`, `dotnet run`, and `dotnet publish`
commands. The source-built CLI remains the low-level contributor workflow at
`src/NetWasm.Compiler.Cli/bin/<Configuration>/net10.0/NetWasm.Compiler.Cli.dll`;
NetWasm does not introduce a parallel `netwasm build/publish` command.

## Build

Versions are pinned in `global.json` and `eng/toolchain.json`:

```sh
dotnet restore NetWasm.slnx
npm ci
eng/verify-toolchain.sh
dotnet build NetWasm.slnx -c Release --no-restore
```

Install and activate Emscripten 6.0.7; it supplies the supported Node.js 24+
and `wasm-ld`/LLD 24+ tools. The native runtime source build also needs CMake,
Ninja, Git, Python 3 and the repository's pinned validation tools. Set
`NETWASM_EMSDK_ROOT` only for repository workflows that intentionally select an
Emscripten root outside the activated shell.

## CLI commands

The default command is compilation. Its required options are
`--input <assembly>`, `--entry Namespace.Type::Method`, and
`--output <core-module>`. Repeat `--reference <assembly>` for the closed
reference closure. `--target wasm32|wasm64` defaults to wasm32.

Optional compile options are `--export name=Namespace.Type::Method`,
`--interop-manifest`, `--host-adapter`, `--runtime-layout`,
`--diagnostic-log`, `--diagnostic-trace`, `--stack-trace-symbols`, `--wit`, `--world`, and
`--source`. `[JSImport]`/`[JSExport]` declarations describe the generated raw
JavaScript boundary; WIT declarations require a selected world.

`--diagnostic-log <path>` enables the structured compiler `ILogger` stream
and writes JSON Lines to that file. `--diagnostic-trace <path>` enables the
full compiler support trace and automatically writes the same log to
`<path>.passes/compiler.log.jsonl` unless an explicit diagnostic-log path is
also supplied. Both facilities are disabled by default. See
[compiler diagnostics](diagnostics.md#compiler-support-bundles) before
sharing these artifacts.

`--stack-trace-symbols <path>` enables opt-in managed stack-trace
instrumentation and writes its symbol sidecar. It is not DWARF/source-level
debug information; see the [support status and roadmap](support-status.md#managed-stack-traces).

The separate `NetWasm.Wit.Bindings.Tool` package writes reflection-free C#
bindings; the compiler CLI does not generate C# source. Install and invoke it
through the ordinary .NET tool workflow:

```sh
dotnet tool install --global NetWasm.Wit.Bindings.Tool --version 0.1.0-rc.1
netwasm-wit-bindgen --wit service.wit --world service --output Bindings.g.cs
```

Omit `--world` when the document contains exactly one world. The tool carries
the pinned platform-neutral wasm-tools module and uses Node.js 24+ from the
activated Emscripten SDK; no native `wasm-tools` installation is required.

`componentize` accepts required `--core-module`, `--wit`, `--output`, and
`--manifest`, plus optional `--runtime-module`, `--world`, `--target`,
`--interop-manifest`, `--jco-version`, and `--preview2-shim-version`. Current
Component output is wasm32 + WASI 0.2; wasm64 fails with `NW1010` rather than
downgrading. The SDK validates generated products with its packaged,
integrity-checked wasm-tools module.

## Runtime and hosts

Build the matching runtime from the compiler’s layout:

```sh
eng/build-netwasm-runtime.sh \
  --runtime-layout build/netwasm/runtime-layout.json \
  --target wasm32 --configuration release \
  --output build/netwasm/runtime.wasm
```

Raw core output runs through the generated JavaScript adapter in Node or a
browser served over HTTP, with explicit `consumerModules`. Components run in
a host implementing declared WIT/WASI imports (for example Wasmtime) or can
be transpiled for a browser with the pinned `jco`. The end-to-end scripts are
host/validation references, not an application SDK.
