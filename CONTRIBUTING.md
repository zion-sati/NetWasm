# Contributing to NetWasm

NetWasm is a compiler, runtime and CoreLib project. A useful change begins with
a small reproducible case and makes the affected boundary explicit: CIL input,
validation, reachability, emitted Wasm, runtime ABI, CoreLib semantics, or host
interop.

## Local setup

Use the pinned versions in `global.json` and `eng/toolchain.json`. Set
`NETWASM_EMSDK_ROOT` to the pinned Emscripten installation before the toolchain
check, as shown in the [quickstart](QUICKSTART.md):

```sh
dotnet restore NetWasm.slnx
npm ci
eng/verify-toolchain.sh
dotnet build NetWasm.slnx -c Release --no-restore
```

Run `eng/build-packages.sh` from a clean checkout to reproduce the complete
core package set. The command stages only the checked-in locked Toolchain
closures and the checksum-pinned official wasm-tools WASI archive; it never
publishes packages.

The default verifier checks only that source-build contract. Contributors who
run the complete cross-host qualification matrix can install the additional
exact WABT, wasm-tools and Wasmtime versions from `eng/toolchain.json`, select
the repository's exact Node version, and run
`eng/verify-toolchain.sh --qualification`.

Run the smallest relevant test project or end-to-end fixture before proposing a
change. Compiler changes should include a focused regression that demonstrates
the input and expected behaviour. CoreLib ports must retain upstream headers
and licence notices.

## Scope and review

- Keep public behaviour, generated output, and tests in the same reviewable
  change where practical.
- Do not add a dependency or host capability without documenting the resulting
  target boundary.
- Do not remove or rewrite upstream licence, copyright, or provenance notices.
- Do not commit build output, coverage, generated diagnostics, local paths, or
  editor/session state.
- Do not treat the current implementation as a promise of desktop-.NET
  compatibility.

Every NetWasm-specific user-visible change must update its owning page under
`docs/` in the same change. This includes diagnostics, target/output and
Component Model/WASI boundaries, CLI options/artifacts, CoreLib/runtime
deviations, host/deployment contracts, and intentional limitations. Feature
documentation lands with the feature; do not document unfinished SDK, package,
HTTP, DI, or other capabilities as available. Run `bash docs/verify-docs.sh`.

Use the repository's issue tracker for reproducible bugs and narrowly scoped
proposals after the public host is available. The project currently has no
contributor licence agreement or contributor-assignment policy published here.
