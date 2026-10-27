# CLI, build, and deployment entry points

For application development, see the [SDK quickstart](sdk-quickstart.md).
This page covers source builds and the compiler, `nwc`, built from source at
`src/NetWasm.Compiler.Cli/bin/<Configuration>/net10.0/nwc.dll`.

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

## The compiler, nwc

`nwc` compiles one managed assembly to a core WebAssembly module. Its command
line follows `csc`: an option starts with `-` (or `/`), takes its value after a
colon, a switch ends in `+` or `-`, and `@file` reads more options from a
response file.

```sh
nwc -out:application.wasm -r:NetWasm.CoreLib.dll -runtimelayout:layout.json App.dll
```

`-target:exe`, the default, compiles an executable from the assembly's entry
point, `-target:library` a library, and `-target:function` a module entered
through the static method `-entry:Namespace.Type::Method` names.
`-export:name=Namespace.Type::Method` exports more static methods. `-r:<assembly>`
references the closed reference closure; repeat it or separate files with `,`
or `;`. `-platform:wasm32|wasm64` defaults to wasm32 and
`-optimize:None|O0|O1|O2|O3|Os|Oz` to Oz.

The sidecars are written only when an option names them: `-interopmanifest`,
`-runtimelayout`, `-compilermetadata`, `-exceptiontypemap` and
`-stacktracesymbols`. Native callbacks need `-runtimelayout`; their object file
is the output's name with `.callbacks.o` unless `-callbackobject:<file>` names
it. `-wit` and `-world` select the WIT package and world, and `-source` names a
source file for stack traces. `[JSImport]`/`[JSExport]` declarations describe
the generated raw JavaScript boundary; WIT declarations require a selected
world.

`nwc` prints its banner unless `-nologo`, reports a failure as
`error NW1234: message`, the form MSBuild reads as a build error, and exits
with code 1. `nwc -help` lists every option.

`-diagnosticlog:<path>` enables the structured compiler `ILogger` stream
and writes JSON Lines to that file. `-diagnostictrace:<path>` enables the
full compiler support trace and automatically writes the same log to
`<path>.passes/compiler.log.jsonl` unless an explicit diagnostic-log path is
also supplied. Both facilities are disabled by default. See
[compiler diagnostics](diagnostics.md#compiler-support-bundles) before
sharing these artifacts.

`-stacktracesymbols:<path>` is opt-in managed stack-trace instrumentation:
it writes the symbol sidecar needed to resolve captured managed frames. It is
not DWARF or source-level debug information. See the [support status and
roadmap](support-status.md#managed-stack-traces).

The separate `NetWasm.Wit.Bindings.Tool` package writes reflection-free C#
bindings; the compiler CLI does not generate C# source. Install and invoke it:

```sh
dotnet tool install --global NetWasm.Wit.Bindings.Tool
netwasm-wit-bindgen --wit service.wit --world service --output Bindings.g.cs
```

Omit `--world` when the document contains exactly one world. `--wit` also
accepts a WIT package directory, or `-` to read standard input, and `--output -`
writes to standard output. `--namespace` sets the generated namespace and
`--accessibility public|internal` the generated types' accessibility. The tool carries
the pinned platform-neutral wasm-tools module; no native `wasm-tools`
installation is required. Unlike an SDK application build, this standalone
command needs Node.js 24+ on `PATH`, through `NETWASM_NODE_PATH` or through
`EMSDK_NODE`.

The optional `--accessibility public|internal` selects generated top-level
accessibility (default: public). See [WIT-to-C# bindings](wit-bindings.md) for a
complete contract, type mappings, dependencies, and regeneration.

`nwc componentize` accepts required `-coremodule`, `-wit`, `-out`, and
`-manifest`, plus optional `-runtimemodule`, `-world`, `-platform`,
`-interopmanifest`, `-jcoversion`, and `-preview2shimversion`. Current
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
