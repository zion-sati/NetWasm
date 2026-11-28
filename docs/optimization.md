# Optimization reference

`NetWasmOptimization` picks a level, and each level is a set of individual
optimizations. A project can turn any one of them on or off without changing
the level, to trade speed against size or to rule one out while looking into a
problem.

## Levels

| Level | Use it for | Default |
| --- | --- | --- |
| `None`, `O0` | Fast builds while you edit; no optimization | `O0` in Debug |
| `O1` | Quick optimized builds | |
| `O2` | Speed, with less code growth than `O3` | |
| `O3` | The fastest code | |
| `Os` | Small code, with no managed method inlining | |
| `Oz` | Small code that still inlines calls inside loops | `Oz` outside Debug |

```xml
<PropertyGroup>
  <NetWasmOptimization>O3</NetWasmOptimization>
</PropertyGroup>
```

Every level except `None` and `O0` runs Binaryen's `wasm-opt` over the linked
module with the matching preset. Two settings change that step:
`NetWasmOptimizerConverge=true` repeats the passes until the module stops
shrinking, and `NetWasmOptimizerLowMemoryUnused=true` lets Binaryen fold
constant address offsets into loads and stores. Both are off by default; see
the [configuration reference](configuration.md#optimization).

## Individual optimizations

Each property below takes `Auto` (the default: the level decides), `Enabled`
or `Disabled`. `Enabled` applies the optimization even at a level that leaves
it off, `Disabled` leaves it out at every level. A few take another value too,
listed after the table.

```xml
<PropertyGroup>
  <NetWasmOptimization>Oz</NetWasmOptimization>
  <NetWasmManagedLoopVectorization>Enabled</NetWasmManagedLoopVectorization>
</PropertyGroup>
```

| Property | What it does | On at |
| --- | --- | --- |
| `NetWasmManagedAddressTakenValuePromotion` | Keeps a value-type local whose address is taken in Wasm locals when the address does not escape, structs with reference fields included. | `O3`, `Oz` |
| `NetWasmManagedAllocationElimination` | Removes allocations, and computed values, that nothing reads. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedAllocationScalarReplacement` | Replaces an object that never escapes its method with a local per field. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedArrayBoundsCheckElimination` | Leaves out array bounds checks a loop's range proves unnecessary. | `O3`, `Oz` |
| `NetWasmManagedArrayLoopIdioms` | Turns loops that fill or copy arrays into bulk memory operations. | `O1`, `O2`, `O3` |
| `NetWasmManagedArrayStoreCheckElimination` | Skips the covariant array store check where the element type is exact. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedBranchPrefixDuplication` | Duplicates a short code prefix into each branch that jumps to it; size-limited at `Os` and `Oz`. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedByRefNullCheckElimination` | Leaves out the null check of a reference (`ref`) the compiler proves non-null: the address of a local, argument, static, array element or field, or one already used. A span loop is versioned: a test before it runs a copy without checks when none of the elements it reads is at address 0, and the loop as written otherwise. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedConditionalSelects` | Turns a branch whose arms only compute a number into a `select`. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedConditionalStoreMerging` | Stores once to a field that both arms of an `if` store to. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedConstantArgumentPropagation` | Specializes a method for constant arguments every caller passes. | `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedConstantCallFolding` | Folds string and UTF-8 comparisons of constants at compile time. | `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedDevirtualization` | Calls the implementation directly when the object's exact type is known. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedDispatcherElimination` | Structures reducible control flow without a dispatch loop. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedEmptyFinallyRemoval` | Removes `finally` handlers left with nothing to run. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedFieldLoadReuse` | Reuses a field's value read earlier when nothing can have changed it. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedIdempotentLoopCollapse` | Runs a counted loop whose iterations all repeat the first one once. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedInlineArrayData` | Addresses one-dimensional array elements from the array itself instead of through its data pointer. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedIntegerCheckElimination` | Leaves out overflow and divide-by-zero checks the operands' ranges rule out. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedLoopInvariantExpressionHoisting` | Moves an expression that does not change inside a loop to before the loop. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedLoopInvariantFieldHoisting` | Reads a field a loop never writes once, before the loop; `NullChecks` also moves its null check. | `O2`, `O3`, `Oz` |
| `NetWasmManagedLoopRotation` | Tests a loop's condition at its end, with one test before entry. | `O3`, `Oz` |
| `NetWasmManagedLoopStoreForwarding` | Keeps an array element a loop updates in place in a local until the loop ends. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedLoopStrengthReduction` | Steps an address through a loop instead of recomputing it from the counter. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedLoopUnrolling` | Unrolls small loops with a known trip count. | `O3` |
| `NetWasmManagedLoopVectorization` | Runs element-wise array loops, and sums, minimums and maximums, with 128-bit SIMD. | `O2`, `O3` |
| `NetWasmManagedMethodInlining` | Inlines managed methods: `AggressiveInlining` ones at `O1`, also calls in loops at `O2` and `Oz` (`Loops`), and whatever pays for speed at `O3`. | `O1`, `O2`, `O3`, `Oz` |
| `NetWasmManagedMulticastInlineDispatch` | Calls a single-cast delegate directly instead of through the multicast loop. | `O1`, `O2`, `O3` |
| `NetWasmManagedNewObjectNullCheckElimination` | Leaves out null checks on objects `new` just created. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedPureCallReuse` | Reuses the result of a pure `Math` call repeated with the same arguments. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedQuotientReuse` | Computes a remainder from the quotient of the same division. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedRangeBranchFolding` | Removes a branch whose outcome the operands' ranges decide. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedScalarCallPreservation` | Plans how long numeric values in a method stay live across calls, so the engine spills fewer registers around them. | `O3` |
| `NetWasmManagedStaticArrayPopulationElimination` | Builds a static array of constant elements as data instead of running the code that fills it. | `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedStaticDataViewElimination` | Reads a `ReadOnlySpan<T>` over constant data directly, without building the span. | `O2`, `O3`, `Oz` |
| `NetWasmManagedStructScalarReplacement` | Splits a struct local used field by field into a local per field. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedSwitchBitTests` | Tests a bit mask for a `switch` with one or two targets. | `O1`, `O2`, `O3`, `Os`, `Oz` |
| `NetWasmManagedUnwrittenStaticFieldFolding` | Reads a static field no code writes as its default value. | Every level |

`None` and `O0` turn every one of them off except
`NetWasmManagedUnwrittenStaticFieldFolding`, which runs at every level so that
every level reaches the same host imports.

Other values:

- `NetWasmManagedMethodInlining`: `Loops` inlines `AggressiveInlining`
  methods and calls inside loops, as `O2` and `Oz` do.
- `NetWasmManagedLoopInvariantFieldHoisting`: `NullChecks` also moves the
  field's null check before the loop, as `O3` and `Oz` do; `Enabled` is the
  narrower form `O2` uses.
- `NetWasmManagedAddressTakenValuePromotion`: `References` also promotes
  structs with reference and byref fields, as `O3` and `Oz` do.

`NetWasmManagedInliningHints` decides how `AggressiveInlining` is treated:
`Auto` follows the level, `Preserve` always honors it, `IgnoreAggressive` never
does.

## Checking a result

An optimization is never meant to change what a correct program computes. If a program
behaves differently at one level than at `O0`, disabling optimizations one at
a time finds the one involved; please
[report it](https://github.com/zion-sati/NetWasm/issues) with the property
that changes the result.

For measured speed and size at each level, see
[performance](performance.md) and [size measurements](size-and-methodology.md).
