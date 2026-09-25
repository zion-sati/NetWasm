# C# language support and focused-test matrix

NetWasm consumes Roslyn CIL; it does not parse C# keywords itself. A keyword is
therefore covered according to the CIL/runtime shape it creates, not by adding a
test whose only purpose is to contain the token. This matrix is based on the
current Microsoft C# keyword reference and the repository's pinned
`-langversion:latest` compiler.

## Coverage rules

- `semantic`: execute observable behavior through generated Wasm.
- `shape`: inspect/validate the distinct Roslyn CIL or metadata shape and also
  execute a representative program.
- `compile`: Roslyn erases the construct and an ordinary compiled workload is
  sufficient; no compiler keyword-specific path exists.
- `library`: syntax lowers to ordinary calls and is supported only when the
  selected CoreLib API exists.
- `unsupported`: deliberately unavailable and documented; Roslyn or NetWasm
  must reject it deterministically rather than hang, trap, or silently compile
  it incorrectly.

Coverage percentages are secondary. Completion requires focused boundary,
negative, exceptional, exact-GC, Debug-CIL, optimized-CIL, wasm32/wasm64, and
LTO tests for every materially distinct shape.

## Reserved keywords

| Keywords | Kind | Focused evidence or decision |
| --- | --- | --- |
| `if`, `else`, `switch`, `case`, `default`, `while`, `do`, `for`, `foreach`, `break`, `continue`, `goto`, `return` | semantic/shape | control-transfer and structurizer suites; forward/backward labels, loops, switches, conditional EH entry/exit |
| `try`, `catch`, `finally`, `throw` | semantic/shape | managed EH suites, filters, nested dispatch, identity, cleanup replacement and GC roots |
| `checked`, `unchecked` | semantic/shape | integer/conversion boundary suites and exact managed overflow classes |
| `lock` | semantic/shape | single-thread `Monitor` semantics and guaranteed finally; blocking/threaded use is deliberately unsupported |
| `using` | semantic/shape | namespace/import erasure plus synchronous disposal on normal and exceptional exits |
| `fixed`, `stackalloc`, `sizeof`, `unsafe` | semantic/shape | pointer width, indirect access, pinned roots, stack overflow, wasm32/wasm64 |
| `as`, `is`, `typeof` | semantic/shape | RTTI identity, null, failed/successful cast and interface/value/reference paths |
| `new`, `this`, `base`, `virtual`, `override`, `abstract`, `sealed`, `interface` | semantic/shape | allocation, constructor, virtual/interface dispatch, new-slot/base-call and null behavior |
| `delegate`, `event`, `operator`, `implicit`, `explicit` | semantic/shape | delegate construction/invocation/multicast, event accessors and conversion/operator calls |
| `ref`, `out`, `in`, `params`, `readonly`, `volatile` | semantic/shape | `LanguageSurfaceCompilationTests` plus by-reference suites cover aliasing/copyback, readonly/value receivers, params arrays and the single-thread volatile boundary; decoder and typed-stack tests retain and validate `volatile.` placement |
| `class`, `struct`, `enum`, `namespace`, `public`, `private`, `protected`, `internal`, `static`, `const`, `extern` | compile/shape | metadata identity, accessibility enforced by Roslyn, static storage and constants; extern only through documented imports |
| `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`, `float`, `double`, `decimal`, `string`, `object`, `void` | semantic | focused primitive, Decimal, string, object/RTTI and target-width suites |
| `true`, `false`, `null` | semantic | primitive/reference constants, branches, equality and null exceptions |

## Contextual keywords

| Keywords | Kind | Focused evidence or decision |
| --- | --- | --- |
| `async`, `await` | semantic/shape | Task/ValueTask state machines, completed and suspended paths, exceptions/cancellation and exact roots |
| `yield`, including `yield return` and `yield break` | semantic/shape | synchronous and asynchronous iterator state machines, early completion and disposal/finally |
| `get`, `set`, `init`, `required`, `field`, `value`, `add`, `remove` | compile/semantic | property/event accessor calls, initialization contracts, field-backed properties and event mutation |
| `record`, `with` | semantic/shape | generated copy/equality/hash/dispatch behavior |
| `when`, `and`, `or`, `not` | semantic/shape | exception filters and recursive pattern matching |
| `var`, `nameof` | compile | compile-time inference/string production; no distinct runtime mechanism |
| `from`, `where`, `select`, `join`, `on`, `equals`, `into`, `let`, `orderby`, `ascending`, `descending`, `group`, `by` | library | query syntax is tested against the query-pattern calls available in the selected managed library; full LINQ is a later library surface |
| `global`, `alias`, `file`, `args` | compile | namespace/file/program source transformations; no distinct runtime mechanism; `file` classification is language-version dependent |
| `managed`, `unmanaged` | shape | function-pointer calling convention and generic constraint metadata; unsupported callable shapes receive deterministic diagnostics |
| `nint`, `nuint` | semantic | target-width integer operations and wasm32/wasm64 boundary suites |
| `scoped`, `allows`, `notnull` | compile/shape | ref-safety and generic constraint metadata; no runtime reflection contract |
| `closed`, `safe` | unsupported by pinned compiler | these are C# 15 preview contextual keywords in the current online reference; the pinned .NET 10/C# 14 compiler rejects them before NetWasm and they must be revisited when the SDK pin moves |
| `dynamic` | unsupported | requires the DLR/runtime binder and reflection; it is outside the supported profile |
| `extension` | compile/library | C# 14 extension declarations lower to ordinary static members; only the referenced managed APIs are retained |
| `partial` | compile | declarations are merged by Roslyn; defining declarations without an implementation follow normal C# rules |

Attribute-target words such as `assembly`, `module`, `type`, `method`,
`property`, and `param` are source-level selectors. Attributes required by
Roslyn-generated code are provided explicitly; general runtime attribute
inspection remains outside the reflection-free profile.

## Required focused suites

1. Declaration/dispatch metadata: modifiers, nested/file types, interfaces,
   overrides, operators, conversions, delegates and events.
2. Structured control flow: every branch/loop/switch form, forward/backward
   `goto`, `goto case/default`, EH boundary exits and Debug-CIL irreducible SCCs.
3. Resource lifetime: `using`, `lock`, `fixed`, synchronous iterators,
   `IAsyncDisposable`, `await foreach`, and async iterators on normal, early and
   exceptional exits.
4. Expressions and patterns: casts/type tests, checked contexts, nullable and
   recursive/logical patterns, switch expressions and target-typed constructs.
5. Generated code: records, lambdas/local functions, async state machines,
   iterator state machines, interpolated strings and collection/query lowering.
6. Unsafe and target width: pointers, function pointers, `sizeof`, `stackalloc`,
   byrefs and managed interior roots on wasm32 and wasm64.
7. Deliberate exclusions: `dynamic`, expression trees, reflection-backed
   features, threads/workers and unsupported interop signatures must fail at a
   stable compile boundary with no partial Wasm artifact.

The focused suite is implemented by `StatementCompilationTests`,
`ControlTransferCompilationTests`, `PatternCompilationTests`,
`ModernLanguageCompilationTests`, `LanguageSurfaceCompilationTests`,
`LambdaCompilationTests`, `IteratorCompilationTests`, `AsyncCompilationTests`,
`ValueTaskCompilationTests`, `LockCompilationTests`, the unsafe/by-reference
suites, and `UnsupportedBoundaryCompilationTests`. The latter freezes Roslyn
compile-time failures for `dynamic`, expression trees, query syntax without an
optional LINQ library, managed threads and unsupported unmanaged call shapes.
