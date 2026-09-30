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

Selecting a newer .NET behavior is scoped to the named operation. It does not
opt the rest of CoreLib into an unqualified future runtime profile.

## Specification-permitted differences

These results are not compiler bugs because C#, ECMA-335, or the relevant .NET
API contract does not require the more specific desktop result.

| Area | NetWasm behavior | Specification or boundary |
| --- | --- | --- |
| Unchecked signed minimum `% -1` | May produce zero or throw; desktop exception identity is not required. | C# 12.13.3-4 and ECMA-335 III.3.55 permit this latitude. |
| `ulong` to `float` | May use intermediate precision, so exact desktop result bits are not promised. | C# 10.2.3 and ECMA-335 I.12.1.3. |
| wasm64 `nuint` to `float` | May use intermediate precision, so exact desktop result bits are not promised. | C# 10.2.3 and ECMA-335 I.12.1.3. |
| `MathF.Round` for already-integral large values | Does not promise CoreCLR's adjacent-bit result from its staged-scaling implementation. | The API result, rather than CoreCLR's internal algorithm, is the contract. |
| `Math`/`MathF` `Sin` and `Tan` of negative zero | Exact negative-zero result bits can vary with the target math implementation. | The API does not promise architecture-independent desktop result bits. |
| Finite `Math.Pow` results | Exact result bits can vary with the target math implementation. | The API does not promise architecture-independent desktop result bits. |
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
