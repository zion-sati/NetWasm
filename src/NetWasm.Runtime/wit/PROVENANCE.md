# Native runtime WIT provenance

`world.wit` is NetWasm-owned composition: it declares the native runtime's
WASI 0.3 dependencies independently of an application's WIT world.

The declarations under `deps/` are a reduced adaptation of the repository's
vendored WASI 0.3.2 contracts, not byte-for-byte upstream copies and not a
complete implementation of those packages. The upstream source is
`https://github.com/WebAssembly/WASI`, release `v0.3.2`, revision
`bdadbbb8cadd287cc5014c4b5a0bf76efc60291c`, as recorded in
`wit/wasi-0.3.2/PROVENANCE.md` at the repository root. See the adjacent
`LICENSE.md` for the retained WASI attribution.

| Runtime declaration | Vendored source under `wit/wasi-0.3.2/` |
| --- | --- |
| `wasi:cli/environment.get-environment` | `cli/environment.wit` |
| `wasi:cli/types.error-code`, `stdout.write-via-stream`, `stderr.write-via-stream` | `cli/stdio.wit` |
| `wasi:cli/exit.exit` | `cli/exit.wit` |
| `wasi:random/random.get-random-u64` | `random/random.wit` |

The adaptation retains the required package versions, function signatures and
the error-code case order. It omits unused functions, documentation and
stability annotations. These omissions describe this consumer's import
requirements, not a replacement WASI host or permission to fake unsupported
operations.
