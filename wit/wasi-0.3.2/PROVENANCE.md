# WASI 0.3.2 WIT provenance

This directory vendors the WASI 0.3.2 packages that NetWasm's toolchain and
Component Model integrations build against.

| Field | Frozen value |
| --- | --- |
| Upstream repository | `https://github.com/WebAssembly/WASI` |
| Upstream release | `v0.3.2` |
| Upstream revision | `bdadbbb8cadd287cc5014c4b5a0bf76efc60291c` |
| Package identities | `wasi:cli@0.3.2`, `wasi:clocks@0.3.2`, `wasi:filesystem@0.3.2`, `wasi:http@0.3.2`, `wasi:random@0.3.2`, `wasi:sockets@0.3.2` |
| License record | `LICENSE.md`, copied from the tagged upstream source |

Each `<package>/*.wit` file is byte-for-byte upstream
`proposals/<package>/wit/*.wit` at that revision. WASI 0.3 has no `wasi:io`
package: streams and futures are Component Model types. Presence in the
toolchain closure does not make a capability reachable: closed-world analysis
and explicit host grants still determine emitted imports. No WIT revision may
be substituted, auto-mapped, or silently upgraded.
