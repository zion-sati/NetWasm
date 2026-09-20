# NetWasm license map

This map covers every source, build, test, WIT, and distribution input in the
repository. The complete license texts live under
[`LICENSES/`](LICENSES/); retained upstream notices control copied material
where they are more specific than a path default.

| Path | License |
| --- | --- |
| src/NetWasm.Compiler*/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Sdk/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Sdk.Pack/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Toolchain/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Hosting*/** | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Testing.VSTest/** | LicenseRef-NetWasm-Community-1.0 |
| tools/** | LicenseRef-NetWasm-Community-1.0 |
| eng/** | LicenseRef-NetWasm-Community-1.0 |
| tests/** | LicenseRef-NetWasm-Community-1.0 |
| build and repository tooling metadata | LicenseRef-NetWasm-Community-1.0 |
| src/NetWasm.Ref/** | MIT |
| src/NetWasm.Templates/** | MIT |
| src/NetWasm.CoreLib/** | MIT |
| src/NetWasm.Runtime/** | MIT |
| src/NetWasm.Runtime.Pack/** | MIT |
| src/NetWasm.Runtime.Wasm32/** | MIT |
| src/NetWasm.Runtime.Wasm64/** | MIT |
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

The Linux `NetWasm.HostTools` NuGet packages also redistribute Ubuntu GCC
`libatomic.so.1` under GPL-3.0 with the GCC Runtime Library Exception. Each
package carries its copyright, license text, and exact corresponding-source
locations under `licenses/`; the binary is not checked into this source tree.
