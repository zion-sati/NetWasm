# NetWasm WIT binding tool

`netwasm-wit-bindgen` converts a selected WIT world into deterministic,
reflection-free C# source. It is separate from the NetWasm CIL compiler: the
compiler consumes the resulting ordinary managed assembly and does not invoke
or identify this generator.

The tool requires the pinned `wasm-tools` executable to be available on `PATH`.

```sh
netwasm-wit-bindgen \
  --wit service.wit \
  --world example:service@1.0.0/service \
  --output Bindings.g.cs
```

Omit `--world` when the WIT document contains exactly one world.
