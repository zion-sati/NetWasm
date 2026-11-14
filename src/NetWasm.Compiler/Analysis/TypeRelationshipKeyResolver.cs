using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Resolves the keys that bound which pairs <see cref="TypeRelationshipClassifier"/> can
/// find assignment compatible, following the same hierarchy it walks: base types,
/// interface closures, object for managed references, and System.Array with the array
/// interfaces for arrays. Classifying every candidate against every target made type
/// tests, dispatch and assignability metadata quadratic in the program's types.
/// </summary>
internal sealed class TypeRelationshipKeyResolver(
    ITypeFinder types,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver identities,
    IImplementedInterfaceResolver interfaces,
    IBaseTypeResolver baseTypes) : ITypeRelationshipKeyResolver
{
    private const string ArrayKey = "array";
    private const string ObjectKey = "identity:primitive:object";
    private static readonly ImmutableArray<string> ArrayInterfaces =
    [
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
    ];
    private static readonly IReadOnlySet<string> NoKeys = ImmutableHashSet<string>.Empty;

    private readonly ConcurrentDictionary<CliTypeIdentity, TypeRelationshipKeys> _keys = [];
    private readonly ConcurrentDictionary<CliTypeIdentity, TypeRelationshipKeys> _hierarchies = [];
    private IReadOnlySet<string>? _resolvedArrayKeys;

    public TypeRelationshipKeys Resolve(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _keys.GetOrAdd(type, Compute);
    }

    private TypeRelationshipKeys Compute(CliTypeIdentity type)
    {
        var keys = Hierarchy(type);
        if (keys.Unbounded)
        {
            return keys;
        }
        // Hierarchy has resolved every type, interface and base these steps resolve.
        var arguments = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal);
        foreach (var instantiation in Instantiations(type))
        {
            AddArguments(arguments, Key(instantiation), AdmittedArguments(instantiation));
        }
        if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array && type.ElementType is { } element)
        {
            // T[] implements IList<U> where T[] converts to U[]: a reference element converts to its hierarchy's
            // types, and a value element to the same-sized primitives and enums, so those admit any argument.
            var admitted = element.IsValueType ? null : HierarchyKeys(element);
            foreach (var name in ArrayInterfaces)
            {
                AddArguments(arguments, DefinitionKey(types.FindType(name).Key), admitted);
            }
            // Array targets share the array key and are filed by their element, which converts the same way.
            AddArguments(arguments, ArrayKey, admitted);
        }
        return keys with { Arguments = arguments, ArgumentKey = ArgumentKey(type) };
    }

    // The type's own key and the keys of its hierarchy, without argument facts.
    private TypeRelationshipKeys Hierarchy(CliTypeIdentity type) => _hierarchies.GetOrAdd(type, static (type, self) =>
    {
        string own;
        try
        {
            own = self.Key(type);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new("unresolved:" + type.CanonicalName, NoKeys, Unbounded: true);
        }
        if (type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation or
            CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.Primitive))
        {
            return new(own, NoKeys, Unbounded: true);
        }
        try
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal) { own };
            // Of the shapes keyed, every reference type is an object.
            if (!type.IsValueType)
            {
                reachable.Add(ObjectKey);
            }
            self.AddHierarchy(reachable, type);
            if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
            {
                reachable.UnionWith(self.ArrayKeys());
            }
            return new(own, reachable, Unbounded: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(own, NoKeys, Unbounded: true);
        }
    }, this);

    private IReadOnlySet<string>? HierarchyKeys(CliTypeIdentity type) =>
        Hierarchy(type) is { Unbounded: false } keys ? keys.Reachable : null;

    // The generic instantiations among the type, its base types and their interfaces.
    private IEnumerable<CliTypeIdentity> Instantiations(CliTypeIdentity type)
    {
        if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            yield break;
        }
        var visited = new HashSet<CliTypeIdentity> { type };
        var pending = new Stack<CliTypeIdentity>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current.Shape == CliTypeShape.GenericInstantiation && !current.TypeArguments.IsDefaultOrEmpty)
            {
                yield return current;
            }
            foreach (var implemented in interfaces.GetInterfaces(current))
            {
                if (visited.Add(implemented))
                {
                    pending.Push(implemented);
                }
            }
            if (baseTypes.Resolve(current) is { } baseType && visited.Add(baseType))
            {
                pending.Push(baseType);
            }
        }
    }

    private IReadOnlySet<string>? AdmittedArguments(CliTypeIdentity instantiation)
    {
        var argument = instantiation.TypeArguments[0];
        var variances = typeDefinitions.ResolveTypeIdentity(instantiation).GenericParameterVariances;
        var variance = variances.IsDefaultOrEmpty ? CliGenericVariance.Invariant : variances[0];
        if (!argument.IsValueType && variance == CliGenericVariance.Contravariant)
        {
            return null;
        }
        if (!argument.IsValueType && variance == CliGenericVariance.Covariant)
        {
            return HierarchyKeys(argument);
        }
        return Hierarchy(argument) is { Unbounded: false } keys
            ? new HashSet<string>(StringComparer.Ordinal) { keys.Own }
            : null;
    }

    private string? ArgumentKey(CliTypeIdentity type)
    {
        var argument = type.Shape switch
        {
            CliTypeShape.GenericInstantiation when !type.TypeArguments.IsDefaultOrEmpty => type.TypeArguments[0],
            CliTypeShape.SzArray or CliTypeShape.Array => type.ElementType,
            _ => null,
        };
        return argument is not null && Hierarchy(argument) is { Unbounded: false } keys ? keys.Own : null;
    }

    private static void AddArguments(
        Dictionary<string, IReadOnlySet<string>?> arguments, string definition, IReadOnlySet<string>? admitted)
    {
        if (!arguments.TryGetValue(definition, out var existing))
        {
            arguments.Add(definition, admitted);
        }
        else if (existing is not null)
        {
            arguments[definition] = admitted is null
                ? null
                : new HashSet<string>(existing.Concat(admitted), StringComparer.Ordinal);
        }
    }

    // The array key, System.Array's hierarchy, and the generic interfaces an array implements
    // over its element type. Arrays share them, so they are resolved once.
    private IReadOnlySet<string> ArrayKeys()
    {
        if (Volatile.Read(ref _resolvedArrayKeys) is { } resolved)
        {
            return resolved;
        }
        var keys = new HashSet<string>(StringComparer.Ordinal) { ArrayKey, ObjectKey };
        AddHierarchy(keys, identities.GetTypeIdentity(types.FindType("System.Array").Key));
        foreach (var name in ArrayInterfaces)
        {
            keys.Add(DefinitionKey(types.FindType(name).Key));
        }
        Volatile.Write(ref _resolvedArrayKeys, keys);
        return keys;
    }

    // The type, its base types and every interface they implement, as the classifier's
    // base-type walk and interface closure reach them.
    private void AddHierarchy(HashSet<string> keys, CliTypeIdentity type)
    {
        var visited = new HashSet<CliTypeIdentity> { type };
        var pending = new Stack<CliTypeIdentity>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            keys.Add(Key(current));
            if (current.Shape is not (CliTypeShape.SzArray or CliTypeShape.Array))
            {
                foreach (var implemented in interfaces.GetInterfaces(current))
                {
                    if (visited.Add(implemented))
                    {
                        pending.Push(implemented);
                    }
                }
            }
            if (baseTypes.Resolve(current) is { } baseType && visited.Add(baseType))
            {
                pending.Push(baseType);
            }
        }
    }

    private string Key(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.SzArray or CliTypeShape.Array => ArrayKey,
        CliTypeShape.Named or CliTypeShape.GenericInstantiation =>
            DefinitionKey(typeDefinitions.ResolveTypeIdentity(type).Key),
        _ => "identity:" + type.CanonicalName,
    };

    private static string DefinitionKey(EntityKey key) =>
        $"definition:{key.Assembly.Name}#{key.MetadataToken:x8}";

}
