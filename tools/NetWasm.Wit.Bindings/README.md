# NetWasm WIT binding tool

`netwasm-wit-bindgen` converts a selected WIT world into deterministic,
reflection-free C# source. It is separate from the NetWasm CIL compiler: the
compiler consumes the resulting ordinary managed assembly and does not invoke
or identify this generator.

Install the tool at the NetWasm version your project builds with, in a local
tool manifest:

```sh
dotnet new tool-manifest
dotnet tool install NetWasm.Wit.Bindings.Tool --version <version>
```

Update it to the same version when you upgrade NetWasm, and regenerate. The
second line of every generated file names the generator version. A NetWasm
project can instead generate its bindings during the build with a `NetWasmWit`
item, which always matches the project's NetWasm.

The package carries NetWasm's pinned platform-neutral `wasm-tools` module. The
standalone command requires Node.js 24 or newer on `PATH`, or you can set
`NETWASM_NODE_PATH` to its executable. It does not require Emscripten or a native
`wasm-tools` installation.

```sh
dotnet netwasm-wit-bindgen \
  --wit service.wit \
  --world example:service@1.0.0/service \
  --output Bindings.g.cs
```

Omit `--world` when the WIT document contains exactly one world.
