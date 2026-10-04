# NetWasm.Ref

`NetWasm.Ref` supplies the metadata-only compile-time contract for the current
`netwasm0.1` profile. It contains the tested
`NetWasm,Version=v0.1` reference asset and no runtime implementation. It also
supplies Microsoft's unmodified stock `LibraryImport` source generator and its
support/resource assemblies as ordinary NuGet analyzer assets. The SDK discovers
them automatically; consumers need no desktop targeting pack or manual analyzer
paths. Unsupported marshalling remains outside the static-native scalar profile.

The NetWasm SDK consumes this package privately for a `netwasm0.1` inner build.
The linker receives implementation assemblies from the selected NetWasm runtime
pack.

The Ref producer uses the repository-pinned .NET SDK's exact targeting-pack
selection. Packaging verifies the resolved generator bytes against the matching
official upstream archive and carries its license/third-party notices plus
`provenance/import-generator.json`. No desktop reference assemblies or unrelated
generators are bundled. Update and qualify this closure with the SDK pin.
