using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

/// <summary>Starts one reachability closure's generic-cycle bookkeeping.</summary>
internal interface IGenericCycleSessionFactory
{
    IGenericCycleSession Create();
}

/// <summary>One closure's record of how method instantiations grow along the methods that lead to them.</summary>
internal interface IGenericCycleSession
{
    /// <summary>
    /// Records a newly discovered method instance, reached from the named instances already recorded, and returns the
    /// body that replaces it when its instantiation has grown past the limit along a generic cycle; otherwise null.
    /// </summary>
    CilMethodBody? Admit(MethodInstanceModel method, IEnumerable<string> parents);
}

// A generic cycle is a method that, through the methods it reaches, comes back to its own definition with a larger
// instantiation. PLINQ's SortQueryOperator<T, TKey>.CreateOrderedEnumerable<TKey2> allocates a
// SortQueryOperator<T, Pair<TKey, TKey2>>, whose CreateOrderedEnumerable then answers the same dispatch with a larger
// one; Join's partition keys grow the same way through IPartitionedStreamRecipient<T>.Receive<TKey>. A closed world
// cannot tell how many times the program goes round, so the closure would never end. As native AOT does, the growth
// is cut at a depth and a breadth: an instance that has grown Limit times along its discovery chain, or a grown
// instance past the first Breadth of its method definition, gets a body that throws NotSupportedException. The program
// compiles, and only one that really reaches such an instance fails, when it does. Breadth matters where arguments grow
// independently: Join's Receive grows in its left and right keys, whose combinations multiply at each level, and every
// binary operator in a program multiplies them again. Discovery runs level by level, so the shallow instances real
// programs reach come first. An instantiation the code writes out, however deep, is not reached by growing and is
// never cut. Growth means the new instantiation is larger and built from the earlier one: one of its type arguments
// properly contains the earlier argument in the same position (Pair<TKey, TKey2> contains TKey), or the two share a
// generic or array part (Join builds ConcatKey<Pair<A, B>, C> from ConcatKey<A, B>). A larger, unrelated one is not:
// an async ValueTask<int> method awaiting an async ValueTask<GeneratedTestCase[]> one reaches
// AsyncValueTaskMethodBuilder<TResult>.Start<TStateMachine> again with a larger instantiation, and counting that cut
// ordinary async code once a program had sixteen such calls. Growth also counts only in the type parameters the
// method's own code names, the members and types its instructions use, as a cycle carries an instantiation onward only
// through those. AsyncTaskMethodBuilder<TResult>.Start<TStateMachine> never names TResult, so a builder for (T,
// Exception) reached after one for T (as every assertion in TUnit.Assertions does, for every T a suite asserts on) has
// not grown through Start. A method whose body cannot be read counts every parameter, as before.
internal sealed class GenericCycleSessionFactory(
    ITypeFinder typeFinder,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    ICalleeMethodBodyReader bodies,
    int limit = GenericCycleSessionFactory.DefaultLimit,
    int breadth = GenericCycleSessionFactory.DefaultBreadth) : IGenericCycleSessionFactory
{
    /// <summary>How many times an instantiation may grow along a cycle before its method is cut.</summary>
    public const int DefaultLimit = 4;

    /// <summary>How many grown instances of one method definition are compiled before the rest are cut.</summary>
    public const int DefaultBreadth = 16;

    private readonly ITypeFinder _typeFinder = typeFinder ?? throw new ArgumentNullException(nameof(typeFinder));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolver _methodInstances =
        methodInstances ?? throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly ICalleeMethodBodyReader _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    private readonly int _limit = limit > 0
        ? limit
        : throw new ArgumentOutOfRangeException(nameof(limit), limit, "The generic cycle limit must be positive.");
    private readonly int _breadth = breadth > 0
        ? breadth
        : throw new ArgumentOutOfRangeException(nameof(breadth), breadth, "The generic cycle breadth must be positive.");

    public IGenericCycleSession Create() => new Session(_limit, _breadth, ExceptionConstructor, NamedParameters);

    // NotSupportedException(string), which the replacement body throws.
    private MethodInstanceModel ExceptionConstructor()
    {
        // ".ctor" names only instance constructors; the type initializer is ".cctor".
        var constructor = _typeFinder.FindType("System.NotSupportedException").Methods
            .Select(_methods.GetMethod)
            .Where(static method => method.Name == ".ctor")
            .Single(static method => method.Signature.ParameterTypes.AsSpan().SequenceEqual([CliValueKind.ManagedReference]));
        return _methodInstances.ResolveMethodInstance(
            constructor.Key.Assembly, constructor.Key.MetadataToken, _symbols.Format(constructor), 0);
    }

    // Which of a method's type parameters (its declaring type's, then its own) the code of its definition names in the
    // methods, fields and types its instructions use; null when the body cannot be read. A callee's signature is in the
    // callee's own parameters, so only its declaring type and method arguments are this method's.
    private ImmutableArray<bool>? NamedParameters(MethodInstanceModel method)
    {
        if (!_bodies.TryReadMethodBody(method.Definition, out var body))
        {
            return null;
        }
        var declaring = method.DeclaringType.TypeArguments.Length;
        var named = new bool[declaring + method.MethodArguments.Length];
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.MethodInstance { Value: var callee }:
                    Mark(callee.DeclaringType);
                    foreach (var argument in callee.MethodArguments)
                    {
                        Mark(argument);
                    }
                    break;
                case CilOperand.FieldInstance { Value: var field }:
                    Mark(field.DeclaringType);
                    break;
                case CilOperand.TypeIdentity { Value: var type }:
                    Mark(type);
                    break;
                case CilOperand.ExactType { Value: var type }:
                    Mark(type);
                    break;
            }
        }
        return [.. named];

        void Mark(CliTypeIdentity type)
        {
            var position = type.Shape switch
            {
                CliTypeShape.GenericTypeParameter => type.GenericParameterIndex,
                CliTypeShape.GenericMethodParameter => declaring + type.GenericParameterIndex,
                _ => -1,
            };
            if ((uint)position < (uint)named.Length)
            {
                named[position] = true;
            }
            foreach (var child in Children(type))
            {
                Mark(child);
            }
        }
    }

    private static ImmutableArray<CliTypeIdentity> Children(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.GenericInstantiation => type.TypeArguments,
        CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer or CliTypeShape.Modified => [type.ElementType!],
        _ => [],
    };

    // The nearest instance of a method definition on a discovery chain: its instantiation's type arguments (the declaring
    // type's, then the method's) and how many times the definition had grown to reach it.
    private readonly record struct Occurrence(ImmutableArray<CliTypeIdentity> Arguments, int Growth);

    private sealed class Session(
        int limit,
        int breadth,
        Func<MethodInstanceModel> exceptionConstructor,
        Func<MethodInstanceModel, ImmutableArray<bool>?> namedParameters) : IGenericCycleSession
    {
        private readonly Dictionary<EntityKey, ImmutableArray<bool>?> _named = [];
        // Each recorded instance's chain: the nearest occurrence of every method definition that led to it, itself
        // included. Chains share structure with their parents' chains.
        private readonly Dictionary<string, ImmutableDictionary<EntityKey, Occurrence>> _chains =
            new(StringComparer.Ordinal);
        private readonly Dictionary<CliTypeIdentity, int> _sizes = [];
        private readonly Dictionary<EntityKey, int> _grown = [];
        private MethodInstanceModel? _exceptionConstructor;

        public CilMethodBody? Admit(MethodInstanceModel method, IEnumerable<string> parents)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(parents);
            ImmutableArray<CliTypeIdentity> arguments = [.. method.DeclaringType.TypeArguments, .. method.MethodArguments];
            var chain = ImmutableDictionary<EntityKey, Occurrence>.Empty;
            var growth = 0;
            var chosen = false;
            // Of several routes to the method, the one along which it has grown most counts.
            foreach (var parent in parents)
            {
                if (!_chains.TryGetValue(parent, out var parentChain))
                {
                    continue;
                }
                var parentGrowth = parentChain.TryGetValue(method.Definition.Key, out var previous) &&
                    Grew(arguments, previous.Arguments, Named(method))
                        ? previous.Growth + 1
                        : 0;
                if (!chosen || parentGrowth > growth)
                {
                    chain = parentChain;
                    growth = parentGrowth;
                    chosen = true;
                }
            }
            _chains[method.CanonicalName] = chain.SetItem(method.Definition.Key, new(arguments, growth));
            if (growth == 0)
            {
                return null;
            }
            var grown = _grown[method.Definition.Key] = _grown.GetValueOrDefault(method.Definition.Key) + 1;
            return growth >= limit || grown > breadth ? Throwing(method) : null;
        }

        private CilMethodBody Throwing(MethodInstanceModel method)
        {
            var constructor = _exceptionConstructor ??= exceptionConstructor();
            var message = $"{method.CanonicalName} is not compiled: its generic instantiation grows through its own " +
                $"code, and NetWasm stops such growth after {limit} levels or {breadth} instances.";
            return new CilMethodBody(method.Definition, 1, [],
            [
                new(0, 1, CilOperation.LoadString, new CilOperand.UserString(message)),
                new(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
                new(2, 3, CilOperation.Throw, new CilOperand.None()),
            ])
            {
                MethodInstance = method,
            };
        }

        private ImmutableArray<bool>? Named(MethodInstanceModel method)
        {
            if (!_named.TryGetValue(method.Definition.Key, out var named))
            {
                named = namedParameters(method);
                _named.Add(method.Definition.Key, named);
            }
            return named;
        }

        // Larger, and built from the earlier instantiation, in the parameters the method's code names: an argument
        // properly contains the earlier argument in its position (Box<T> to Box<Box<T>>), or rebuilds the same generic
        // type around a generic or array part of it (PLINQ's Join builds ConcatKey<Pair<A, B>, C> from ConcatKey<A, B>).
        // Two instantiations of different generic types that share a part (the state machines of two async methods
        // over List<int>) are not one built from the other.
        private bool Grew(
            ImmutableArray<CliTypeIdentity> arguments,
            ImmutableArray<CliTypeIdentity> previous,
            ImmutableArray<bool>? named)
        {
            bool Counts(int index) => named is not { } positions || positions.ElementAtOrDefault(index);

            var size = 0;
            var previousSize = 0;
            for (var index = 0; index < arguments.Length; index++)
            {
                size += Counts(index) ? Size(arguments[index]) : 0;
            }
            for (var index = 0; index < previous.Length; index++)
            {
                previousSize += Counts(index) ? Size(previous[index]) : 0;
            }
            if (size <= previousSize)
            {
                return false;
            }
            for (var index = 0; index < arguments.Length && index < previous.Length; index++)
            {
                if (Counts(index) && !arguments[index].Equals(previous[index]) && Contains(arguments[index], previous[index]))
                {
                    return true;
                }
            }
            for (var index = 0; index < arguments.Length && index < previous.Length; index++)
            {
                if (Counts(index) && SameGenericType(arguments[index], previous[index]))
                {
                    var parts = new HashSet<CliTypeIdentity>();
                    foreach (var child in Children(previous[index]))
                    {
                        AddParts(parts, child);
                    }
                    if (Children(arguments[index]).Any(child => SharesPart(child, parts)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SameGenericType(CliTypeIdentity type, CliTypeIdentity other) =>
            type.Shape == CliTypeShape.GenericInstantiation && other.Shape == CliTypeShape.GenericInstantiation &&
            type.ElementType!.Equals(other.ElementType) ||
            type.Shape is CliTypeShape.SzArray or CliTypeShape.Array && type.Shape == other.Shape;

        private void AddParts(HashSet<CliTypeIdentity> parts, CliTypeIdentity type)
        {
            if (Size(type) > 1 && parts.Add(type))
            {
                foreach (var child in Children(type))
                {
                    AddParts(parts, child);
                }
            }
        }

        private bool SharesPart(CliTypeIdentity type, HashSet<CliTypeIdentity> parts) =>
            Size(type) > 1 && (parts.Contains(type) || Children(type).Any(child => SharesPart(child, parts)));

        private bool Contains(CliTypeIdentity type, CliTypeIdentity part) =>
            Size(type) >= Size(part) && (type.Equals(part) || type.Shape switch
            {
                CliTypeShape.GenericInstantiation => type.TypeArguments.Any(argument => Contains(argument, part)),
                CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
                    CliTypeShape.UnmanagedPointer or CliTypeShape.Modified => Contains(type.ElementType!, part),
                _ => false,
            });

        // The number of nodes in a type's tree: a generic instantiation counts itself and its arguments.
        private int Size(CliTypeIdentity type)
        {
            if (_sizes.TryGetValue(type, out var size))
            {
                return size;
            }
            size = 1 + type.Shape switch
            {
                CliTypeShape.GenericInstantiation => type.TypeArguments.Sum(Size),
                CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
                    CliTypeShape.UnmanagedPointer or CliTypeShape.Modified => Size(type.ElementType!),
                _ => 0,
            };
            _sizes.Add(type, size);
            return size;
        }
    }
}
