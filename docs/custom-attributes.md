# Bounded custom attribute queries

Status: available.

NetWasm can resolve a custom attribute query during compilation when its target
and attribute filter each identify one exact, closed type. The resulting program
executes ordinary constructor calls and assignments for the selected attributes.
It does not need a runtime attribute database.

```csharp
using System.ComponentModel;
using System.Reflection;

[Description("An order")]
sealed class Order;

static class Example
{
    public static void Query()
    {
        bool present = typeof(Order).IsDefined(typeof(DescriptionAttribute), false);
        DescriptionAttribute? one = typeof(Order).GetCustomAttribute<DescriptionAttribute>();
        IEnumerable<DescriptionAttribute> many = typeof(Order).GetCustomAttributes<DescriptionAttribute>();
        object[] explicitFilter = typeof(Order).GetCustomAttributes(typeof(DescriptionAttribute), false);
    }
}
```

User-defined attribute classes are supported, including closed generic attributes
such as `LabelAttribute<int>`. A filter may also be an open generic attribute type,
such as `typeof(LabelAttribute<>)`: as on .NET, it finds every attribute whose type,
or a base of it, is an instance of it, and returns them as `Attribute[]`. Flags and
Description use the same query mechanism.
`DescriptionAttribute` also provides its ordinary constructors, protected
`DescriptionValue`, `Default`, equality, hashing and default-attribute behavior.
This does not imply support for `TypeDescriptor`, property-grid discovery or
automatic localization.

## Accepted calls and type provenance

| Call | Supported contract |
| --- | --- |
| `type.IsDefined(typeof(A), inherit)` | Returns existence without constructing attributes. |
| `type.GetCustomAttribute<A>([inherit])` | Returns one instance or null; multiple matches throw `AmbiguousMatchException`. |
| `type.GetCustomAttributes<A>([inherit])` | Returns the selected instances as an enumerable. |
| `type.GetCustomAttributes(typeof(A), inherit)` | Returns an array of selected instances. |
| Filtered non-generic `CustomAttributeExtensions` and `Attribute` helpers | The same bounds apply to singular, plural and existence overloads. |
| Filtered calls through `MemberInfo` or `ICustomAttributeProvider` | The receiver must still be proven to identify a type declaration. |

`[inherit]` above denotes an optional overload argument. Omitted inheritance
arguments use the API's normal default of true. A runtime Boolean is allowed;
it selects between direct and inherited results for the same target and filter.

Accepted provenance includes `typeof(KnownType)`, unchanged local copies and
`typeof(T)` after closed generic specialization. A helper using `typeof(T)` can
therefore work for each supported specialization. A `Type` parameter of an
ordinary helper names no exact type; such a query is answered from the program's
run-time types instead, as the next section describes.

The compiler also recognizes a private instance readonly `Type` field initialized
directly from `typeof(T)` in the constructor's initial instruction prefix, before
other field initialization or the base-constructor call. Every constructor must
agree, and the defining assembly must contain no conflicting stores or address escapes.
`Nullable.GetUnderlyingType(field) ?? field` preserves a bounded enum target for
both enum and nullable-enum specializations. Readonly syntax alone is insufficient:
control flow that can bypass or revisit initialization makes the proof fail.

## Targets known only at run time

A query whose target is a `Type` the compiler cannot name is answered too, when
its filter is still one exact, closed attribute type: a `Type` parameter, a
lambda's argument, a field, a call's result, `obj.GetType()`, or
`flag ? typeof(A) : typeof(B)`.

```csharp
static readonly ConcurrentDictionary<Type, bool> Hidden = new();

static bool IsHidden(Type type) =>
    Hidden.GetOrAdd(type, static type => type.IsDefined(typeof(HiddenAttribute), inherit: true));
```

The compiler answers from the types the program already has at run time: the
types it names with `typeof`, allocates, or otherwise gives a `Type` object. The
query compares its target with each of those types that carries the filter
attribute, and answers as the compile-time query for that type would. Any other
type answers as a type without the attribute does: `false`, `null` or an empty
array. A null target throws as on .NET: `NullReferenceException` from an
instance method, and `ArgumentNullException` from `Attribute`'s statics and the
extensions.

The target must be typed `Type`. A `MemberInfo` or `ICustomAttributeProvider`
that the compiler cannot prove to be a `Type`, such as a `MethodInfo`, stays
outside these bounds.

## Semantics and retention

Selection honors attribute assignability, `AttributeUsage`, class inheritance and
multiplicity. Implemented interfaces do not contribute inherited attributes.
Filters may name an abstract attribute base class; matching derived attributes
are selected without constructing the filter itself.

Retrieval creates fresh attribute instances and argument arrays on each call.
Constructors and named field/property assignments execute normally, including
their side effects and exceptions. Enumerating one returned result again reuses
its instances. Singular retrieval constructs its matches before reporting
ambiguity. Constructor exceptions propagate directly; property-setter failures
have the normal `CustomAttributeFormatException` and `TargetInvocationException`
wrapping.

Serialized arguments can contain primitives, strings, enums, Type values, null,
boxed values and one-dimensional arrays permitted by custom attribute metadata.
The reachable code in user constructors and setters must also fit the NetWasm
platform profile.

Retention follows reachable queries:

- No query adds no attribute-query metadata or construction code.
- `IsDefined` needs no attribute constructor, setter or construction payload.
- Retrieval retains selected materialization and its ordinary executable
  dependencies. Unrelated attributes are not retained merely because they exist.
- Constant `inherit: false` excludes base-only results. A runtime inheritance
  argument can retain both bounded result paths.
- A query on a target known only at run time constructs its filter attribute
  only for the run-time types that carry it, and adds no type to the program.

## Queries outside these bounds

A reachable call to one of these APIs outside the bounds compiles, and throws
`PlatformNotSupportedException` with a message naming the query when it runs, so
code that catches it, or never reaches it, keeps working, as a library built for
.NET Standard expects. Such a query never silently returns incomplete results,
and nothing needs a preserve-everything option.

- Filters known only at run time, and targets known only at run time that are
  not typed `Type`, such as a `MethodInfo` or an `ICustomAttributeProvider`.
- Unfiltered enumeration and the catch-all `System.Attribute` filter.
- Open generic targets, array/pointer/byref targets, and member, parameter,
  module or assembly targets. Closed generic type declarations are eligible.
- Attribute query method handles and delegates; use a direct bounded call.
- `CustomAttributeData` discovery and synthesized metadata attributes such as
  `SerializableAttribute`, `ComImportAttribute` and `StructLayoutAttribute`.

A query the program never reaches costs nothing. General runtime reflection and
runtime type discovery remain outside this feature. See [CoreLib/runtime compatibility](corelib-runtime.md)
for the wider platform boundary.
