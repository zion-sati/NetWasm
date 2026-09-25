# Managed fdlibm port

The transcendental implementations in `Math.Sin.cs`, `Math.Cos.cs`,
`Math.Tan.cs`, `Math.Sinh.cs`, `Math.Tanh.cs`, `Math.Acosh.cs`, `Math.Exp.cs`,
`Math.Log.cs`, and the `Math.Fd*` kernels are translated from Sun fdlibm,
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
