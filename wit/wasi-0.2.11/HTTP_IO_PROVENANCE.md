# WASI HTTP/I/O 0.2.11 WIT provenance

This directory vendors the official WASI Preview 2 CLI closure used by the
versioned NetWasm toolchain and its selective Component Model integrations.

| Field | Frozen value |
| --- | --- |
| Upstream repository | `https://github.com/WebAssembly/WASI` |
| Upstream release | `v0.2.11` |
| Upstream revision | `ed73919426173babd88ae145e31deca3d484bbd0` |
| Package identities | `wasi:http@0.2.11`, `wasi:io@0.2.11`, `wasi:clocks@0.2.11`, `wasi:cli@0.2.11`, `wasi:random@0.2.11`, `wasi:filesystem@0.2.11`, `wasi:sockets@0.2.11` |
| License record | `LICENSE.md`, copied from the tagged upstream source |

The copied `.wit` files are byte-for-byte upstream source from that revision.
The retained scope includes the CLI world and the HTTP, I/O, clock, random,
filesystem, and socket packages in its official transitive WIT closure.
Presence in the parser/toolchain closure does not make a capability reachable:
closed-world analysis and explicit host grants still determine emitted imports.
No WIT revision may be substituted, auto-mapped, or silently upgraded.
