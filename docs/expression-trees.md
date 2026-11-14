# Expression-tree execution profile

NetWasm provides a bounded, interpreter-only `System.Linq.Expressions` profile.
It is intended for libraries that construct small predicates, accessors,
numeric transforms, and conditional projections. It is not desktop expression
tree parity and does not add runtime CIL generation, broad reflection,
`dynamic`, or native code generation.

`Expression<TDelegate>.Compile()`, `Compile(bool preferInterpretation)`, and the
corresponding erased `LambdaExpression` calls always select the managed
interpreter. The `preferInterpretation` value does not select a second backend.
`LambdaExpression.CanInterpret` is therefore `true`, while
`LambdaExpression.CanCompileToIL` is `false`.

## Executable boundary

A lambda can take up to 16 parameters, as `Func` and `Action` do, and can return
a value or nothing. Custom delegate types work when their `Invoke` signature has
that shape. Parameters cannot be by-reference.

Parameters, locals, constants, array elements and results can be references
(classes, interfaces, arrays and delegates), any value type (the primitives,
`decimal`, enums, `DateTime`, user structs), or the nullable form of a value
type. The interpreter holds every value boxed, as upstream's interpreter does.
By-reference-like types (`Span<T>` and other `ref struct` types), pointers,
function pointers and open generic types are not executable.

| Node | Supported shape |
| --- | --- |
| `Parameter`, `Constant` and `Default` | Any admitted value. `Default` of a value type is its zero value. |
| `New` | A constructor the compiler has planned, of a class or a struct; `New(Type)` of a value type, which is its zero value; and `New(typeof(C))` of a class, as C# builds `new C()`, which the compiler gives C's parameterless constructor. |
| Arithmetic | `Add`, `Subtract`, `Multiply` and `Negate` with their checked forms, and `Divide`, `Modulo`, `UnaryPlus`, `Increment` and `Decrement`, on the primitive numeric types and `decimal` (`Negate` on signed types), lifted over nullable operands as C# lifts them. |
| Bitwise and shifts | `And`, `Or`, `ExclusiveOr`, `Not` and `OnesComplement` on integers and `bool`; `LeftShift` and `RightShift` of an integer by an `int`. |
| Comparisons | `LessThan`, `LessThanOrEqual`, `GreaterThan` and `GreaterThanOrEqual` on numeric operands and `decimal`, lifted as C# lifts them. |
| `Equal` and `NotEqual` | Primitive, nullable-primitive and `decimal` equality, reference identity, string value equality, or an executable operator method. |
| Operator methods | A user-defined operator or conversion the compiler has planned runs as a call, and `Power` as `Math.Pow`. Over nullable operands the operator is lifted as C# lifts it: a null operand gives null, `false` for an ordering comparison, and for `==` and `!=` whether both are null. |
| `AndAlso`, `OrElse`, `IsTrue` and `IsFalse` | Built-in Boolean logic with short-circuit evaluation. |
| `Coalesce` | Operands that need no conversion to the result type. |
| `Convert` and `ConvertChecked` | Numeric conversions among the primitive numeric types and `decimal`, boxing and unboxing, reference and interface casts, and nullable wrapping and unwrapping, with the CLR's exceptions. |
| `TypeIs`, `TypeEqual`, `TypeAs` and `Unbox` | Type tests and casts against the program's type table. |
| `Conditional` and `Switch` | A Boolean test, or a switch with case values and an optional static comparison method the compiler has planned, with value or void branches. |
| `Block` and `Assign` | Lexically scoped locals, default-initialized on every invocation; assignment to a parameter, a local, an array element, a field, or a property through a setter the compiler has planned. |
| `MemberInit` and `ListInit` | Member assignments, nested bindings on a member that holds an object, collection bindings, and collection initializers, as C# builds them. A struct initializer starts from a new zero value each time. |
| `Loop`, `Label` and `Goto` | Loops with break and continue labels, and jumps that do not leave a `finally` or `fault` block. |
| `Try` and `Throw` | `catch` without a filter, `finally`, `fault` and rethrow, as upstream's interpreter runs them. |
| Arrays | `NewArrayInit`, one-dimensional `NewArrayBounds`, `ArrayLength`, `ArrayIndex` and `Index` on an array. |
| `MemberAccess` | A readable field, or a parameterless property whose getter the compiler has planned, on a reference or value-type receiver, or static. |
| `Call` | A static method, or an instance method on a reference or value-type receiver, with a result or none, that the compiler has planned. Closed generic declaring types and closed generic methods are supported. |
| `Lambda` (nested) | A lambda inside the tree becomes a delegate of the type it names, such as the predicate of `items.Any(i => i > n)`. It captures the variables of the lambdas around it, which it and they share. |
| `Quote` | The quoted expression as a value, as C# builds a lambda whose type is an expression. A variable it captures is read through the box the closures share. |
| `Invoke` | A lambda, or a quoted lambda, called with arguments: its body runs in place with the arguments as its parameters. |

A variable a nested lambda or quote captures lives in a box shared by the lambda
that defines it and every closure that captures it, so a change made through a
closure is seen by the others, as in compiled C#. Each entry to a block makes
new boxes for its variables, so closures made in a loop keep their own values.
A captured catch variable is the handler's exception, and `Rethrow` still throws
the exception the handler took.

Member receivers can be composed, such as `model.Child.Value`. Ordinary C#
captures work when the generated display-class object is represented as a
constant followed by field reads. Captured values are read when the delegate
runs, so mutation after `Compile()` remains observable.

Method calls use closed-world compiler facts rather than reflection. Static,
nonvirtual, virtual and interface calls are supported. A value-type receiver's
member runs on the value the interpreter holds, so a method that changes its
struct changes that value, as a call on a variable does in compiled code. A
static field or property, or a constructor, runs its type's initializer first,
as an ordinary access does. A virtual call through `object` or an interface on
a boxed enum is not executable, because the enum's `ToString` and its kin are
compiler intrinsics, and compiler intrinsics are not exposed indirectly through
expression execution. Members of a nullable value (`HasValue`, `Value`) are not
executable either: the interpreter holds a nullable as its underlying value's
box.

Extension-method calls use the ordinary static-call expression shape. Their
formatted expression text therefore uses static-call notation rather than
desktop extension-call syntax; this avoids retaining runtime extension-method
metadata.

Evaluation is left-to-right. `AndAlso`, `OrElse`, `Conditional` and `Switch`
execute only the selected path, but all paths are validated before `Compile()`
returns. A method receiver is evaluated before its arguments, and arguments
retain their declared order. Static initialization uses the same once-only
policy as an ordinary static call and occurs after argument evaluation. Numeric
operations preserve signed-zero and NaN behavior where the underlying operation
requires it, and box the exact declared result type. Each invocation owns its
local and evaluation frame, including during reentrant delegate calls.

## Deliberately unsupported shapes

The interpreter rejects the following with `NotSupportedException`:

- lambdas of more than 16 parameters and by-reference parameters;
- a user-defined operator given without its `MethodInfo`, which the factory
  would have to find by name (C# passes the method with the tree);
- assignment to a constant, a read-only field or a field of a nullable type, and nested bindings on a member that
  holds a value type;
- `New(Type)` of a class whose `Type` is only known at run time, which needs a constructor lookup;
- members of a nullable value, and constant (literal) fields read as fields;
- multidimensional `NewArrayBounds`;
- catch filters;
- enum equality and nullable-enum equality without an operator method;
- by-reference-like, pointer, function-pointer and open value shapes; and
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
A lambda's delegate type is one the program names, so `Expression<TDelegate>`
validates and invokes its own `Invoke` signature; a nested lambda's delegate
comes from the same adapter as an outer lambda's. Constructing
`Expression.Invoke` over any other delegate-typed expression, such as a
parameter or a constant, throws `PlatformNotSupportedException`: only the run
knows that delegate's type (see
[runtime type information](support-status.md#runtime-type-information)).
Expression execution by itself does not require member or type names. A
reachable read of `MemberInfo.Name` retains the names of the members the program
demands. A program carries no type names: they read `type#<id>` unless the
build writes them to its type-symbols sidecar (see
[type names](support-status.md#type-names)). This supports metadata-based cache
keys and display names without adding runtime member discovery, `MethodInfo.Invoke`,
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
