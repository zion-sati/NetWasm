# Third-party notices

This repository retains source-level copyright, license, and provenance notices
when copying upstream material. The complete notice text referenced by copied
source comments is available as [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).
The corresponding standard texts are also available under [LICENSES](LICENSES/).

## Restored build tools and runtime archives

Each `NetWasm.HostTools.<host RID>` NuGet package includes the upstream Node.js,
LLVM/LLD, Binaryen and wasm-tools license texts and embedded third-party notices
in its `LICENSE.txt` and `licenses/` entries. Its `nwc` compiler is a Native AOT
executable whose runtime is built with the Satori garbage collector; the notices for
the .NET runtime, its libraries and Satori are in `licenses/nwc-THIRD-PARTY-NOTICES.txt`.
The development-host executables stay in
the NuGet cache and are not copied into an application's publish output.
The Linux host packages also carry Ubuntu GCC `libatomic.so.1`, its GPL-3.0
and GCC Runtime Library Exception notices, and exact corresponding-source
locations in `licenses/`.
`NetWasm.Runtime.Pack` includes the Emscripten, musl libc and LLVM compiler-rt
notices for its static archive inputs in its own
[`LICENSE.txt`](src/NetWasm.Runtime.Pack/runtime/LICENSE.txt). `Math` and
`MathF` call musl's math functions from that C library, as .NET calls the
platform's C library.

## Managed transcendental math

Where a managed port gives the C library's results bit for bit and runs
faster, `Math` and `MathF` call it in place of the C library. `Math.Asin` and
`Math.Acos` are C# ports of Sun fdlibm, pinned to `biosbits/fdlibm` commit
`dfd9eaed985332a1c3af98c2bab4004eea217d3d`, and retain the permissive Sun
Microsystems notices from 1993 and 2004. `double.Exp2`, `Math.Pow`,
`MathF.Exp`, `MathF.Log`, `MathF.Log2` and `MathF.Pow` port Arm's optimized
routines as musl v1.2.5 carries them (commit
`0784374d561435f7c787a555aeab8ede699ed298`), under the MIT license with the
"Copyright (c) 2018, Arm Limited" and "Copyright (c) 2017-2018, Arm Limited"
notices. `MathF.Acosh` ports musl's own `acoshf.c` under musl's MIT license,
and `MathF.Cbrt` and the `log1pf` that `MathF.Acosh` uses port musl's
`cbrtf.c` and `log1pf.c`, FreeBSD msun code that retains the 1993 Sun
Microsystems notice. Every derived file
identifies its upstream files; the complete notices appear in the source files
and in `THIRD-PARTY-NOTICES.TXT`.

## .NET Foundation sources

Selected .NET runtime and CoreLib sources retain their `.NET Foundation` MIT
headers and remain subject to those notices in addition to the repository's
MIT path assignment. Ported framework libraries are maintained separately. The
micro-benchmark harness's two helpers copied from dotnet/performance, under
`compiler-qualification/micro-benchmarks/shim/`, retain their `.NET
Foundation` MIT headers; the benchmarks themselves are read from a
dotnet/performance checkout and are not part of this repository.

## FluentValidation source adaptation

The expression-tree compatibility fixture under
`tests/end-to-end/expression-fluentvalidation-consumer/` source-adapts a
bounded excerpt of FluentValidation 12.1.0. It retains the .NET Foundation and
contributors copyright notice and is licensed under Apache-2.0. The complete
license text is available as
[`LICENSES/Apache-2.0.txt`](LICENSES/Apache-2.0.txt).

## Float conversion and hashing code

`NetWasm.CoreLib` contains code identified in source as derived from
`m-ou-se/floatconv`, copyright 2020 Mara Bos, and the xxHash32 algorithm
published by Yann Collet. Those source notices identify BSD-2-Clause terms.

## Decimal floating-point code

Several decimal binary128 implementation files retain notices for the Intel
Decimal Floating-Point Math Library. Source also identifies
`sinpi`/`cospi`/`tanpi` lineage from `amd/aocl-libm-ose`. Those notices identify
BSD-3-Clause terms.

## IANA Time Zone Database

`src/NetWasm.Toolchain/timezones/zoneinfo` holds the zones of IANA tz release
2026c, compiled with that release's `zic`, with its `tzdata.zi` and `version`
files. The tz database is in the public domain. NetWasm.Toolchain packages these
files, and published applications include the zones they select.

## WASI description files

`wit/wasi-0.2.11` is copied with its upstream `LICENSE.md` and provenance.
WASI dependencies below NetWasm WIT worlds follow the same upstream terms.
Those terms apply rather than the repository's default MIT path assignment.

## wit-parser test inputs

`tests/NetWasm.Wit.Bindings.Tests/Conformance/wit-parser` copies the WIT inputs
of the Bytecode Alliance wit-parser 0.256.0 test suite unchanged, with the
crate's `LICENSE-MIT` and a provenance note. They are used under MIT, one of
the crate's license options.
