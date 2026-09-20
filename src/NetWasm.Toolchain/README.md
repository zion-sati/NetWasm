# NetWasm.Toolchain

`NetWasm.Toolchain` contains platform-neutral upstream assets used by the
NetWasm build and deployment boundary. It carries the
pinned Binaryen JavaScript closure, the audited jco 1.28.1 transpile entry point,
the Preview 2 shim 0.24.1, the official wasm-tools 1.256.0 WASI module and
runner, compiler-owned JavaScript commands and WIT contracts. Every entry point
and supporting file is selected from the immutable package manifest and
verified by SHA-256 before use.

The jco files live at `tools/jco/node_modules/...` with no native-RID
projection. The package qualifies the upstream
`@bytecodealliance/jco/dist/jco.js` `transpile` command. Its lock-driven
transpile-only closure retains the complete supported optional native binding
set required by the selected runtime path. The separately inventoried Rolldown
1.2.4 closure supplies the package-owned bundler used to emit one selected,
minified Hosting bundle. Componentization, optimization and unrelated
dynamically loaded command families remain outside the package contract. The
closure locks, integrity inventories and third-party notices are packaged beside
their entry points.

The native Node, wasm-ld, wasm-merge and wasm-opt executables live in the
separate `NetWasm.HostTools.<host RID>` package selected by an application or
test project's SDK. The platform-neutral wasm-tools WASI module is bundled
here. Wasmtime is optional and is not required by the standard SDK build, run
or publish path. Builds do not download native tools.

The package retains the pinned source WIT closures and their provenance
manifests under `tools/wit`, `tools/async-wit` and `tools/compiler-wit`.
The SDK consumes their deterministic binary WIT products under
`tools/wit-packages`, whose own manifest binds each product to its source
manifest and the pinned wasm-tools generator. This avoids host-dependent source
directory traversal without changing the selected worlds or adding a native
tool/RID projection. No network fetch is needed.

Maintainers regenerate these checked-in products with
`python3 eng/regenerate-toolchain-wit-packages.py` after staging the pinned
wasm-tools module (or setting `NETWASM_WASM_TOOLS_MODULE_PATH`). The generator
builds each product twice and refuses nondeterministic output.

Consumers provide the resolved package root and use only manifest-verified
absolute asset paths. This package does not search `PATH`, invoke npm/npx,
download tools, contain native executables or silently fall back to another
package or ambient installation.
