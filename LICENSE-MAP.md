# NetWasm license map

This map covers every source, build, test, WIT, and distribution input in the
repository. The complete license texts live under
[`LICENSES/`](LICENSES/); retained upstream notices control copied material
where they are more specific than a path default. Where several rows match a
path, the most specific row applies.

The NetWasm Community License covers Tooling: the compiler and the
surrounding tools required to create a user's deployable package, with their
tests and qualification harnesses. Everything else is MIT, including all code
a user needs to deploy an application and the JavaScript that ships with it;
NetWasmGC and CompactGC are Apache-2.0.

| Path | License |
| --- | --- |
| src/NetWasm.Compiler*/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Sdk/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Sdk.Pack/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Toolchain/timezones/zoneinfo/** | public domain (IANA Time Zone Database) |
| src/NetWasm.Toolchain/wit/deps/wasi-tls/** | Apache-2.0 WITH LLVM-exception (Wasmtime's wasi:tls draft WIT) |
| src/NetWasm.Toolchain/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Hosting.Build/** | LicenseRef-NetWasm-Community-1.0 |
| tools/NetWasm.TimeZones/** | MIT |
| tools/NetWasm.Hosting.Generator/** | MIT |
| tools/** | LicenseRef-NetWasm-Community-1.0 |
| compiler-qualification/** | LicenseRef-NetWasm-Community-1.0 |
| tests/end-to-end/expression-fluentvalidation-consumer/** | Apache-2.0 |
| tests/NetWasm.Wit.Bindings.Tests/Conformance/wit-parser/** | MIT (upstream wit-parser test inputs) |
| tests/libraries/** | MIT |
| tests/NetWasm.Runtime.Tests/** | MIT |
| tests/NetWasm.Runtime.Pack.Tests/** | MIT |
| tests/NetWasm.Hosting.Tests/** | MIT |
| tests/NetWasm.Ref.Tests/** | MIT |
| tests/NetWasm.Testing.VSTest.Tests*/** | MIT |
| tests/NetWasm.TimeZones.Tests/** | MIT |
| tests/NetWasm.Hosting.Generator.Tests/** | MIT |
| tests/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Ref/** | MIT |
| src/NetWasm.Testing.VSTest/** | MIT |
| eng/** | MIT |
| build and repository tooling metadata | MIT |
| src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/raw-adapter-runtime*.mjs | MIT |
| src/NetWasm.Hosting/** | MIT |
| src/NetWasm.JSImportGenerator/** | MIT |
| src/NetWasm.Templates/** | MIT |
| src/NetWasm.CoreLib/** | MIT |
| src/libraries/** | MIT |
| src/native/external/zlib-ng/** | Zlib |
| src/native/external/brotli/** | MIT |
| src/native/external/openssl/** | Apache-2.0 |
| src/native/** | MIT |
| src/NetWasm.Runtime/collector/compact/** | Apache-2.0 |
| src/NetWasm.Runtime/collector/netwasm/** | Apache-2.0 |
| src/NetWasm.Runtime/** | MIT |
| src/NetWasm.Runtime.Pack/** | MIT |
| src/NetWasm.Runtime.Wasm32/** | MIT |
| src/NetWasm.Runtime.Wasm64/** | MIT |
| wit/netwasm-platform-1.0.0/deps/wasi-tls/** | Apache-2.0 WITH LLVM-exception (Wasmtime's wasi:tls draft WIT) |
| wit/netwasm-*/** | MIT |
| docs/** | MIT |
| public documentation and distribution metadata | MIT |
| wit/netwasm-*/deps/wasi-*/** | upstream WASI license |
| wit/wasi-0.2.11/** | upstream WASI license |

Build and repository tooling metadata means `Directory.Build.props`,
`src/Directory.Build.props`, `tests/Directory.Build.props`, `global.json`, `package.json`,
`package-lock.json`, and `NetWasm.slnx`. Public documentation and distribution
metadata means the root public documents, `.gitignore`, and `.gitattributes`.

The `wit/wasi-0.2.11/LICENSE.md` and provenance files are copied unchanged.
WASI dependency inputs below NetWasm WIT worlds follow those upstream terms as
well. Source files that retain an upstream copyright, license, or provenance
header remain governed by that notice in addition to this path map.

`NetWasm.App.Runtime` redistributes native code built from third-party
sources: zlib-ng and Brotli, vendored under `src/native/external/`, and
OpenSSL, which the build downloads at the release `eng/toolchain.json` pins
and checks against its SHA-256 (only its license text is in this tree). The
package carries each license text under `native/`.

The Linux `NetWasm.HostTools` NuGet packages also redistribute Ubuntu GCC
`libatomic.so.1` under GPL-3.0 with the GCC Runtime Library Exception. Each
package carries its copyright, license text, and exact corresponding-source
locations under `licenses/`; the binary is not checked into this source tree.
