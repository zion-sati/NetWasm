# Managed fdlibm port

The transcendental implementations in `Math.Sin.cs`, `Math.Cos.cs`,
`Math.Tan.cs`, `Math.Sinh.cs`, `Math.Tanh.cs`, `Math.Acosh.cs`, `Math.Asin.cs`,
`Math.Acos.cs`, `Math.Atan.cs`, `Math.Atan2.cs`, `Math.Cbrt.cs`, `Math.Log.cs`,
and the `Math.Fd*` kernels are translated from Sun fdlibm,
`https://github.com/biosbits/fdlibm` at commit
`dfd9eaed985332a1c3af98c2bab4004eea217d3d`. Each derived file retains its
upstream filename, copyright and permissive notice. The numerical coefficients,
argument reduction and evaluation order remain upstream-derived.

The port replaces C word-access macros with endian-independent BitConverter
operations. Constant tables are method-local read-only spans and bounded
scratch buffers use stack allocation. They have no shared Math type initializer,
heap allocation or native dependency. Unused methods and their constants remain
eligible for ordinary compiler reachability and final Wasm dead-code removal.
The twenty-element reduction buffers and sixty-six base-2^24 digits of 2/pi
are the upstream bounds for the supported binary64 domain.

`Math.Exp.cs` ports Arm's optimized exp and exp2 as musl v1.2.5 carries them
(`src/math/exp.c`, `exp2.c` and `exp_data.c`, commit
`0784374d561435f7c787a555aeab8ede699ed298`), keeping the Arm MIT notice. Its
128-entry 2^(k/N) table is an array in a nested class: as a method-local span
it would give each call a value frame (the earlier fdlibm pow measured that at
about five times per call), while the class's initialization check only
reaches exp, exp2 and pow. The hexadecimal coefficients are written as their exact decimal values.

`Math.Pow.cs` ports Arm's optimized pow from the same musl release (`pow.c`
and `pow_data.c`), with its log table as a nested-class array and its exp
kernel sharing `Math.Exp.cs`'s constants, table and special case. Its
worst-case error is 0.54 ulp, so a result that is exactly representable, such
as an integral power that fits in a double, is returned exactly.

`MathF.ExpLog.cs` ports Arm's float exp, log, log2 and pow from the same
release (`expf.c`, `logf.c`, `log2f.c`, `powf.c` and their `exp2f_data.c`,
`logf_data.c`, `log2f_data.c` and `powf_data.c`), which compute in double with
small tables and replace the earlier rounding of the double functions.
Upstream .NET defines `float.Exp2` and `float.Exp10` as `MathF.Pow(2, x)` and
`MathF.Pow(10, x)`, so desktop answers follow the platform's powf; here they
round the double exp2 of x and of x * log2(10) once, which is faster than powf
and within half an ulp. The port matches musl bit for bit on 8 million seeded
and special inputs (an Emscripten build of musl as the reference); musl and
glibc carry the same routines, so results also match Mono and desktop .NET on
Linux. Desktop .NET on macOS uses Apple's libm, which differs from both by one
ulp on 0.02-0.3% of those inputs.

`MathF.Trig.cs` ports musl's float trigonometry from the same release
(`sinf.c`, `cosf.c`, `sincosf.c`, `tanf.c`, their `__sindf`, `__cosdf` and
`__tandf` kernels and `__rem_pio2f.c`), FreeBSD msun code with the 1993 Sun
notice. The medium-size reduction returns only the multiple of pi/2, from
which each caller recomputes the reduced argument, and huge arguments go to
separate methods that call `Math.RemPio2fHuge` and its shared Payne-Hanek
kernel, so the common paths need no value frame.

The algorithms assume the binary64 round-to-nearest arithmetic provided by
WebAssembly. Floating-point status flags are not exposed by the .NET profile;
the returned NaN, infinity, subnormal and signed-zero values are still tested.
`Sqrt` continues to use the existing Wasm square-root instruction.

The existing 402-input accuracy fixture is unchanged. The additional
1,004-input full-range fixture uses independently computed mathematical values
which converge at 2048 and 2304 bits. Both enforce the existing four-ULP budget
for finite nonzero results and check special values separately. This is bounded
regression evidence, not a claim that every transcendental result is correctly
rounded for every binary64 input.

`Math.Fma.cs` ports musl's `fma.c` (musl's own) and `fmaf.c` (FreeBSD msun)
from the same release, the routines Mono links. The double version multiplies
the significands exactly in 128 bits, aligns and adds the addend, and rounds
once on conversion; the float version adds in double and corrects only an
inexact result that lands exactly halfway between two floats. Where the addend
is zero or not finite, the double version answers as desktop's fused
instruction does: a product that underflows keeps its sign, and a signaling NaN
addend comes back quiet. `FusedMultiplyAddCorpusTests` checks rounding,
cancellation, float midpoints and the special values against desktop .NET.
