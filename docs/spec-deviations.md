# Language, CLI, and .NET behavior differences

NetWasm targets a deliberately bounded, closed-world .NET profile. It does not
claim complete desktop .NET, C# implementation, or ECMA-335 implementation
coverage. This page records behavior that is intentionally different, or for
which the applicable specification permits more than one result.

An implementation gap or known compiler defect is not an accepted deviation.
Those remain work to be fixed or are reported in the
[support-status inventory](support-status.md). The focused tests named below
make accepted differences visible and prevent them from silently expanding.

## Profile exclusions

The following capabilities are outside the current NetWasm profile because
their runtime and size costs conflict with its closed-world model:

- broad reflection and reflection-driven code generation;
- `dynamic` binding and runtime assembly loading;
- runtime-created generic types;
- managed threads, a managed thread pool, shared heaps, and blocking waits; and
- implicit globalization, filesystem, process, socket, or browser capabilities.

Code that requires an excluded platform capability may fail compilation or
throw `PlatformNotSupportedException`. A supported API shape with an
unsupported expression, operation, or argument instead uses the documented API
exception, normally `NotSupportedException` or an argument exception. See
[current limitations](LIMITATIONS.md) and the
[CoreLib/runtime reference](corelib-runtime.md) for the complete boundary.

## Deliberately selected .NET behavior

| Area | NetWasm behavior | Difference |
| --- | --- | --- |
| Binary floating point to `decimal` | Uses the .NET 11 exact-input conversion policy for casts, constructors, and `Convert.ToDecimal`. | This also applies to projects using the .NET 10 SDK or C# 14 and earlier. It intentionally differs from the older .NET 10 seven/fifteen-significant-digit policy. |
| Invalid `MidpointRounding` with NaN or infinity | Validates the rounding mode first, matching .NET 11. | .NET 10 validation precedence was unspecified for these combined invalid inputs. |
| Runtime member handles | Requires the exact, explicit compiler-emitted declaring-type context for bounded two-argument handle resolution. | CoreCLR also accepts a default context when the member handle identifies the closed type, and a derived context for some inherited methods. NetWasm rejects both with `ArgumentException`. |
| Base `Attribute` equality and hashing | Uses NativeAOT-shaped object identity semantics. Derived attributes can still override `Equals` and `GetHashCode`. | CoreCLR's base implementation discovers and compares instance fields through reflection. NetWasm does not retain that reflection surface. |
| Type and assembly names | A name without the type-symbols sidecar reads `type#<id>` (`Name`, `FullName`, `ToString()`, the default `Object.ToString()` and framework messages), `Namespace` is `null`, and every assembly name reads `<Assembly name unavailable>`. With `NetWasmTypeNames` the names form as .NET forms them, with that assembly text. See [type names](support-status.md#type-names). | A program carries no type or assembly names, so a type's name costs nothing until a build ships it in a sidecar. |
| Execution-stack sufficiency checks | `RuntimeHelpers.EnsureSufficientExecutionStack` is a no-op in the current single-reactor profile. | NetWasm does not currently maintain the desktop runtime's managed stack-limit probe. |
| Static array initialization fallback | Compiler-recognized RVA initialization is supported. An unrecognized runtime call throws `PlatformNotSupportedException`. | NetWasm does not retain desktop reflection over arbitrary field handles. |
| `DateOnly` and `TimeOnly` general parsing | Uses a deterministic invariant grammar for complete Gregorian dates and ordinary 24-hour or AM/PM times. Dates accept numeric month/day/year or year/month/day forms, English month names in month/day/year or day/month/year forms, optional English weekdays, two-digit years, and `/`, `-`, `.`, comma, or whitespace component separators. Exact parsing and formatting retain their separately supported .NET 11-compatible contracts. | General parsing excludes partial dates and current-date defaults, adjacent tokens, eras, international suffix tokens, alternate reinterpretation after an invalid primary date, and stray leading or trailing punctuation. Use `ParseExact` or `TryParseExact` when another fixed grammar is required. |

Selecting a newer .NET behavior is scoped to the named operation. It does not
opt the rest of CoreLib into an unqualified future runtime profile.

## Specification-permitted differences

These results are not compiler bugs because C#, ECMA-335, or the relevant .NET
API contract does not require the more specific desktop result.

| Area | NetWasm behavior | Specification or boundary |
| --- | --- | --- |
| `ulong` to `float` | May use intermediate precision, so exact desktop result bits are not promised. | C# 10.2.3 and ECMA-335 I.12.1.3. |
| wasm64 `nuint` to `float` | May use intermediate precision, so exact desktop result bits are not promised. | C# 10.2.3 and ECMA-335 I.12.1.3. |
| `MathF.Round` for already-integral large values | Does not promise CoreCLR's adjacent-bit result from its staged-scaling implementation. | The API result, rather than CoreCLR's internal algorithm, is the contract. |
| `Math`/`MathF` `Sin` and `Tan` of negative zero | Exact negative-zero result bits can vary with the target math implementation. | The API does not promise architecture-independent desktop result bits. |
| Finite `Math.Pow` results | Exact result bits can vary with the target math implementation. | The API does not promise architecture-independent desktop result bits. |
| Sign and payload of a NaN result | A NaN the optimizer computes ahead of time is WebAssembly's canonical NaN: positive, quiet, without payload. At run time NaNs propagate as the hardware's do. | WebAssembly leaves a NaN result's sign and payload nondeterministic, and desktop .NET's own NaNs differ by architecture: `0f / 0f` is `0xFFC00000` on x64 and `0x7FC00000` on Arm64. |
| `MultiplyAddEstimate` on `float`, `double` and their vectors | Rounds the product and then the sum, as Mono does. Desktop .NET fuses them into one rounding on hardware with FMA, so the last bit can differ. | The API returns an estimate and does not promise either rounding; desktop results already vary with the processor. |
| Directly emitted invalid open-instance delegates | NetWasm need not accept the malformed construction. | The emitted CIL violates ECMA-335 II.14.6 and III.4.21 preconditions; CoreCLR acceptance is not required behavior. |
| CLI rank-one `T[*]` to `T[]` | Does not promise CoreCLR's zero-bound identity normalization or its derived mutable-address exception. | C# cannot express `T[*]`, and ECMA-335 does not require CoreCLR's normalization. |

## External ABI observability

A host boundary can expose less information than an in-process managed call. A
WIT function whose result type is only an integer cannot transport an arbitrary
managed exception object or preserve desktop exception-type identity. In that
case the Component adapter reports operation failure according to the declared
WIT contract. This is an ABI boundary, not permission for managed code inside a
NetWasm module to lose exception identity.

## Qualification policy

Accepted differences have paired skipped theories or explicit assertions in
`KnownNonBugCorpusPartitioner.cs`. Each skip carries its reason and, where
applicable, its C# or ECMA-335 reference. Additions require all of the following
in the same change:

1. evidence that the behavior is outside the selected profile or permitted by
   the governing contract;
2. a narrowly scoped test and skip reason;
3. a user-visible entry on this page; and
4. confirmation that the case is not masking a compiler or CoreLib defect.

This ledger is intentionally conservative. If the specification requires a
result, implementation cost or WebAssembly instruction availability does not
turn a mismatch into an accepted deviation.
