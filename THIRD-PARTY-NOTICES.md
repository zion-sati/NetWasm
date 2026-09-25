# Third-party notices

This repository retains source-level copyright, licence, and provenance notices
when copying upstream material. The complete notice text referenced by copied
source comments is available as [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).
The corresponding standard texts are also available under [LICENSES](LICENSES/).

## Restored build tools and runtime archives

Each `NetWasm.HostTools.<host RID>` NuGet package includes the upstream Node.js,
LLVM/LLD and Binaryen license texts and embedded third-party notices in its
`LICENSE.txt` and `licenses/` entries. The development-host executables stay in
the NuGet cache and are not copied into an application's publish output.
The Linux host packages also carry Ubuntu GCC `libatomic.so.1`, its GPL-3.0
and GCC Runtime Library Exception notices, and exact corresponding-source
locations in `licenses/`.
`NetWasm.Runtime.Pack` includes the Emscripten, musl libc and LLVM compiler-rt
notices for its static archive inputs in its own
[`LICENSE.txt`](src/NetWasm.Runtime.Pack/runtime/LICENSE.txt).

## .NET Foundation sources

Selected .NET runtime and CoreLib sources retain their `.NET Foundation` MIT
headers and remain subject to those notices in addition to the repository's
MIT path assignment. Ported framework libraries are maintained separately.

## Float conversion and hashing code

`NetWasm.CoreLib` contains code identified in source as derived from
`m-ou-se/floatconv`, copyright 2020 Mara Bos, and the xxHash32 algorithm
published by Yann Collet. Those source notices identify BSD-2-Clause terms.

## Decimal floating-point code

Several decimal binary128 implementation files retain notices for the Intel
Decimal Floating-Point Math Library. Source also identifies
`sinpi`/`cospi`/`tanpi` lineage from `amd/aocl-libm-ose`. Those notices identify
BSD-3-Clause terms.

## Managed transcendental math

The `Math.Sin`, `Math.Cos`, `Math.Tan`, `Math.Sinh`, `Math.Tanh`, `Math.Acosh`,
`Math.Exp`, and `Math.Log` implementations and their `Math.Fd*` kernels are C#
ports of Sun fdlibm. They retain the permissive Sun Microsystems notices from
1993 and 2004. Their source is pinned to `biosbits/fdlibm` commit
`dfd9eaed985332a1c3af98c2bab4004eea217d3d`; every derived file identifies its
upstream filename. Permission to use, copy, modify and distribute is granted
provided the corresponding notice is preserved. The complete notices appear
in the source files and in `THIRD-PARTY-NOTICES.TXT`.

## WASI description files

`wit/wasi-0.2.11` is copied with its upstream `LICENSE.md` and provenance.
WASI dependencies below NetWasm WIT worlds follow the same upstream terms.
Those terms apply rather than the repository's default MIT path assignment.
