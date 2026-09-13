# Third-party notices

This repository retains source-level copyright, licence, and provenance notices
when copying upstream material. The complete notice text referenced by copied
source comments is available as [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).
The corresponding standard texts are also available under [LICENSES](LICENSES/).

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

## WASI description files

`wit/wasi-0.2.11` is copied with its upstream `LICENSE.md` and provenance.
WASI dependencies below NetWasm WIT worlds follow the same upstream terms.
Those terms apply rather than the repository's default MIT path assignment.
