using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private IReadOnlySet<string>? _resolvedArrayKeys;

    public TypeRelationshipKeys Resolve(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _keys.GetOrAdd(type, Compute);
    }

    private TypeRelationshipKeys Compute(CliTypeIdentity type)
    {
        string own;
        try
        {
            own = Key(type);
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
            if (IsManagedReference(type))
            {
                reachable.Add(ObjectKey);
            }
            AddHierarchy(reachable, type);
            if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
            {
                reachable.UnionWith(ArrayKeys());
            }
            return new(own, reachable, Unbounded: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(own, NoKeys, Unbounded: true);
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

    private static bool IsManagedReference(CliTypeIdentity type) =>
        !type.IsValueType &&
        type.Shape is not (CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter or
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer);
}
