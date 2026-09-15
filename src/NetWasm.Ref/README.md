# NetWasm.Ref

`NetWasm.Ref` supplies the metadata-only compile-time contract for the
experimental `netwasm0.1` profile. It contains the tested
`NetWasm,Version=v0.1` reference asset and no runtime implementation.

The NetWasm SDK consumes this package privately for a `netwasm0.1` inner build.
The linker receives implementation assemblies from the selected NetWasm runtime
pack.
