# NetWasm WIT binding tool

`netwasm-wit-bindgen` converts a selected WIT world into deterministic,
reflection-free C# source. It is separate from the NetWasm CIL compiler: the
compiler consumes the resulting ordinary managed assembly and does not invoke
or identify this generator.

Install the tool:

```sh
dotnet tool install --global NetWasm.Wit.Bindings.Tool --version 0.1.0-rc.1
```

The package carries NetWasm's pinned platform-neutral `wasm-tools` module. It
uses Node.js 24 or newer from the activated Emscripten SDK, so no native
`wasm-tools` installation is required.

```sh
netwasm-wit-bindgen \
  --wit service.wit \
  --world example:service@1.0.0/service \
  --output Bindings.g.cs
```

Omit `--world` when the WIT document contains exactly one world.
