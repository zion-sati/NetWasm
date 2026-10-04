# Expression-tree execution profile

NetWasm provides a bounded, interpreter-only `System.Linq.Expressions` profile.
It is intended for libraries that construct small predicates, accessors,
numeric transforms, and conditional projections. It is not desktop expression
tree parity and does not add runtime CIL generation, broad reflection,
`dynamic`, or native code generation.

`Expression<TDelegate>.Compile()`, `Compile(bool preferInterpretation)`, and the
corresponding erased `LambdaExpression` calls always select the managed
interpreter. The `preferInterpretation` value does not select a second backend.

## Executable boundary

An executable lambda has exactly one non-by-reference parameter and a non-void
result. `Func<T, TResult>` and custom delegate types are supported when their
`Invoke` signature has that shape. Parameter and result types can be references
or one of these primitive value types:

- `bool` and `char`;
- `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, and `ulong`; or
- `float` and `double`.

Enums, nullable values, `decimal`, pointers, by-reference values, and other
value types are not executable in this profile. An interface-typed reference
can still contain a boxed value type when an admitted member access dispatches
through that interface.

| Node | Supported shape |
| --- | --- |
| `Parameter` and `Constant` | Admitted primitive or reference values. |
| `Default` | Admitted primitive zero/default values and null references. |
| `Add` | Built-in unchecked `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, and `double` addition. |
| `Negate` | Built-in unchecked `short`, `int`, `long`, `float`, and `double` negation. |
| `GreaterThan` and `LessThan` | Built-in signed, unsigned, and floating comparisons for the `Add` numeric set. |
| `Convert` | Unchecked conversions among signed/unsigned 8-, 16-, 32-, and 64-bit integers, `float`, and `double`. |
| `AndAlso` | Built-in Boolean short-circuit evaluation. |
| `Conditional` | Boolean test with compatible admitted result branches, including void use inside a block. |
| `Block` | Lexically scoped admitted locals, including shadowing and default initialization on every invocation. |
| `Assign` | Assignment to a lambda parameter or block local; the non-void form returns the assigned value. |
| `MemberAccess` | A readable instance field or parameterless property whose receiver is another executable reference expression. |
| `Call` | A bounded static or reference-instance method with admitted primitive or reference arguments and a non-void admitted result. Closed generic declaring types and closed generic methods are supported. |

Member receivers can be composed, such as `model.Child.Value`. Ordinary C#
captures work when the generated display-class object is represented as a
constant followed by admitted field reads. Captured values are read when the
delegate runs, so mutation after `Compile()` remains observable. Direct member
access on a value-type receiver remains outside this profile.

Method calls use closed-world compiler facts rather than reflection. Static,
nonvirtual, virtual, and interface calls are supported. An interface receiver
can contain a boxed value type, but a value type cannot be used as the direct
receiver expression. Every parameter and result must use the admitted value
set above; by-reference parameters and void results are rejected. Compiler
intrinsics are not exposed indirectly through expression execution.

Evaluation is left-to-right. `AndAlso` and `Conditional` execute only the
selected path, but all paths are validated before `Compile()` returns. A method
receiver is evaluated before its arguments, and arguments retain their declared
order. Static initialization uses the same once-only policy as an ordinary
static call and occurs after argument evaluation. Numeric
operations use unchecked semantics, preserve signed-zero and NaN behavior where
the underlying operation requires it, and box the exact declared result type.
Each invocation owns its local/evaluation frame, including during reentrant
delegate calls.

## Deliberately unsupported shapes

The interpreter rejects the following with `NotSupportedException`:

- zero-parameter or multi-parameter lambdas, by-reference parameters, and void
  delegates;
- method-backed operators or conversions and checked conversion nodes;
- constructors, member assignment, delegate invocation, and method calls with
  excluded signatures or receivers;
- nested executable lambdas, `Quote`, and `Invoke`;
- direct value-type member receivers and nonprimitive value-type operations;
- enum, nullable, `decimal`, pointer, and other excluded value shapes; and
- expression node families not listed in the executable table.

This rejection is atomic. An unsupported node in an unselected conditional or
short-circuit path still causes `Compile()` to fail; NetWasm does not publish a
partially lowered interpreter.

## Exceptions and diagnostics

Malformed expression-factory inputs keep their documented argument exceptions.
An unbound `ParameterExpression` and an inconsistent internal interpreter
invariant use `InvalidOperationException`. This includes an impossible mismatch
between the compiler-generated call instruction and its internal argument
array. Missing platform capabilities such
as broad reflection or runtime code generation remain
`PlatformNotSupportedException`; they are different from an unsupported node
inside the admitted expressions API. Exceptions thrown by an invoked property
getter, method target, or static initializer propagate directly and are not
wrapped in `TargetInvocationException`.

## Closed-world retention

Applications that do not reach expression-tree compilation do not retain the
interpreter. Reaching the runtime tree compiler conservatively retains its
admitted instruction families; NetWasm does not promise per-node pruning for a
tree whose shape is created at runtime.

Delegate adapters and member-execution descriptors remain closed-world compiler
facts. Only reachable delegate signatures and reachable members are emitted.
Expression execution by itself does not require member or type names. A
reachable read of `MemberInfo.Name` or `Type.FullName` independently retains
only the demanded name facts. This supports metadata-based cache keys and
display names without adding runtime member discovery, `MethodInfo.Invoke`,
`FieldInfo.GetValue`, `Delegate.CreateDelegate`, Reflection.Emit, or `dynamic`.

## Qualified consumer boundary

The compatibility fixture source-adapts the expression-facing excerpt of
FluentValidation 12.1.0's core property-rule path. It preserves member
extraction from
`Expression<Func<T, TProperty>>`, accessor compilation, expression-text and
member-name cache keys, root-parameter type identity, cache reuse, and accessor
execution. It passes Debug and Release CIL on wasm32 and wasm64.

This is not a claim that the unmodified FluentValidation package runs on the
current NetWasm profile. The package also depends on
`ConcurrentDictionary<TKey, TValue>` and culture/globalization behavior, which
are separate library-profile concerns. Within the retained expression-facing
cache algorithm, the fixture replaces `ConcurrentDictionary<TKey, TValue>`
with `Dictionary<TKey, TValue>` for NetWasm's current single-threaded profile.
The fixture does not reproduce FluentValidation's full rule, validation, or
localization machinery. It does not add managed threading, broad reflection,
or a package-specific compiler path.

The optimized source-adapted consumer is 402,808 bytes after final `-Oz`.
The same qualified tree still produces the byte-identical 84,653-byte Hello42
component when expression trees are unused.
