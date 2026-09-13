# Targets and output formats

The WebAssembly memory width and the packaging format are separate choices.
See the [support status and roadmap](support-status.md) for the canonical
classification of this boundary and the remaining work.

| Output | wasm32 | wasm64 / Memory64 |
| --- | --- | --- |
| `compile` core module | Supported | Supported |
| Raw core module + JavaScript manifest/adapter | Supported | Supported with checked `BigInt` address transport |
| `componentize` Component output | Supported with WASI 0.2 | Rejected with `NW1010` |

`wasm32` uses 32-bit addresses and managed references. `wasm64` uses Memory64
`i64` address/reference transport while semantic IDs and lengths retain their
defined widths. Select the width with `--target`; the default is wasm32. The
runtime layout and native runtime target must match the application target.

## Per-application runtime and memory layout

The runtime pack supplies relocatable archives rather than fixed-address
runtime tiers. Each application records its static-data end in
`runtime-layout.json`; runtime materialization aligns the runtime base using
the manifest's current 16-byte alignment and links the matching target archive
and system-library closure. The manifest validates 4-byte pointers for wasm32
and 8-byte pointers for wasm64, and the linker machine, archive, and layout
must agree.

The native stack is fixed at 64 KiB. The default initial heap is 64 KiB and
initial memory is rounded to the 64 KiB WebAssembly page size. The heap can
grow up to the selected maximum when the host permits it; the stack does not
grow with the heap. Current policy caps are 2 GiB for wasm32 and 8 GiB for
wasm64, not pre-reserved runtime capacity or a wasm64 architectural limit.

Raw core output is the explicit JavaScript host path. The generated manifest
and adapter describe consumer-supplied imports/exports; browser APIs are not
implicit. This path supports both widths.

Component packaging currently uses the wasm32 + WASI Preview 2 (`cm32p2`)
toolchain. The CLI accepts the target spelling `wasm64`, but the capability
check fails before packaging with `NW1010` because the current stable external
encoder/toolchain does not provide an end-to-end Memory64 Component path. The
request is never silently downgraded to wasm32. Internal `cm64p2` names do not
change that support boundary.

WASI 0.3 is independent: a wasm32 transport evolution neither solves nor waits
on the missing `cm64p2` Memory64 Component encoder, canonical ABI, runtime,
and host chain. Current packaging remains WASI 0.2; a wasm32 transport
evolution would not make wasm64 Component output available. User-defined WIT
is synchronous in the current profile. Use wasm32 for Components and wasm64
for a raw core module under its explicit host contract.

## Contributor and qualification rule

Do not triage the known `cm64p2` result as a compiler failure. When a test asks
the current pinned toolchain to package a wasm64 Component and receives
`NW1010` for the unavailable Memory64 Component capability, record that cell as
externally blocked and stop that cell. Do not retry with hidden flags, silently
downgrade it to wasm32, fork or upgrade external tools, substitute raw-core
output for the Component assertion, or infer that WASI 0.3 changes the result.

This stop rule applies only to wasm64 **Component packaging**. The matching raw
wasm64 core module remains a first-class target and must still be compiled,
validated, executed, and semantically asserted. Revisit the Component cell only
when NetWasm deliberately updates its pinned Component toolchain/capability
contract or the project explicitly starts a `cm64p2` enablement phase.
