# Native runtime WIT provenance

`world.wit` is NetWasm-owned composition: it declares the native runtime's
Preview 2 dependencies independently of an application's WIT world.

The declarations under `deps/` are a reduced adaptation of the repository's
vendored WASI 0.2.11 contracts, not byte-for-byte upstream copies and not a
complete implementation of those packages. The upstream source is
`https://github.com/WebAssembly/WASI`, release `v0.2.11`, revision
`ed73919426173babd88ae145e31deca3d484bbd0`, as recorded in
`wit/wasi-0.2.11/HTTP_IO_PROVENANCE.md` at the repository root. See the adjacent
`LICENSE.md` for the retained WASI attribution.

| Runtime declaration | Vendored source under `wit/wasi-0.2.11/` |
| --- | --- |
| `wasi:cli/environment.get-environment` | `cli/environment.wit` |
| `wasi:cli/stdout.get-stdout`, `stderr.get-stderr` | `cli/stdio.wit` |
| `wasi:cli/exit.exit` | `cli/exit.wit` |
| `wasi:io/error.error` resource identity | `io/error.wit` |
| `wasi:io/streams.stream-error`, `output-stream.blocking-write-and-flush` | `io/streams.wit` |

The adaptation retains the required package versions, resource identities,
function signatures, and stream-error variant order. It omits unused functions,
resource methods, documentation and stability annotations. In particular, the
error resource has no imported debug-string method here, and the output-stream
resource retains only the blocking write-and-flush method used by the native
runtime. These omissions describe this consumer's import requirements, not a
replacement WASI host or permission to fake unsupported operations.

The runtime-pack regeneration script embeds this world as component-type
metadata. Any declaration change requires fresh runtime archives, import and
component-metadata checks, and actual component composition/execution tests.
This provenance note alone does not qualify the linked artifacts or prove the
full Preview 1 removal requirement.
