using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class AssignableTypeMetadataBuilder(
    ITypeIdentityResolver identities,
    ITypeDefinitionResolver typeDefinitions,
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys,
    ManagedTypeLayouts types) : IAssignableTypeMetadataBuilder
{

    private readonly (CliTypeIdentity Type, int TypeId)[] _runtimeAssignableTypes =
        types.Objects
            .Select(pair =>
                (Type: identities.GetTypeIdentity(pair.Key), pair.Value.TypeId))
            .Concat(types.ConstructedObjects.Select(pair =>
                (Type: pair.Key, pair.Value.TypeId)))
            .Where(target => IsClosedRuntimeAssignableTarget(
                typeDefinitions,
                target.Type))
            .ToArray();

    // The runtime types filed under their own relationship key with their first type argument's key, and those
    // without a key.
    private Dictionary<string, List<(CliTypeIdentity Type, int TypeId, string? Argument)>>? _targetsByKey;
    private readonly List<(CliTypeIdentity Type, int TypeId)> _unboundedTargets = [];

    public ImmutableArray<int> Build(CliTypeIdentity candidate)
    {
        if (candidate.Shape is CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer ||
            candidate.ContainsGenericParameters ||
            candidate.Shape == CliTypeShape.Named &&
            typeDefinitions.ResolveTypeIdentity(candidate).GenericArity != 0)
        {
            return [];
        }

        // A candidate classifies only the runtime types filed under the keys it reaches.
        return [.. Targets(candidate)
            .Where(target => relationships
                .Classify(candidate, target.Type)
                .IsAssignmentCompatible)
            .Select(target => target.TypeId)
            .Distinct()
            .Order()];
    }

    private IEnumerable<(CliTypeIdentity Type, int TypeId)> Targets(CliTypeIdentity candidate)
    {
        if (_targetsByKey is null)
        {
            _targetsByKey = new(StringComparer.Ordinal);
            foreach (var target in _runtimeAssignableTypes)
            {
                var keys = relationshipKeys.Resolve(target.Type);
                if (keys.Unbounded)
                {
                    _unboundedTargets.Add(target);
                }
                else if (_targetsByKey.TryGetValue(keys.Own, out var filed))
                {
                    filed.Add((target.Type, target.TypeId, keys.ArgumentKey));
                }
                else
                {
                    _targetsByKey.Add(keys.Own, [(target.Type, target.TypeId, keys.ArgumentKey)]);
                }
            }
        }
        var candidateKeys = relationshipKeys.Resolve(candidate);
        if (candidateKeys.Unbounded)
        {
            return _runtimeAssignableTypes;
        }
        // Instantiations of a generic definition the candidate reaches are filtered by their first type argument:
        // every array type would otherwise be classified against every IEnumerable<T>, IList<T> and
        // IReadOnlyList<T> in the program, and Newtonsoft.Json's program held over 12 GB of such pairs.
        return candidateKeys.Reachable
            .SelectMany(key =>
            {
                var filed = _targetsByKey.GetValueOrDefault(key);
                if (filed is null)
                {
                    return [];
                }
                IReadOnlySet<string>? admitted = null;
                var filtered = candidateKeys.Arguments?.TryGetValue(key, out admitted) == true && admitted is not null;
                return filed
                    .Where(target => !filtered || target.Argument is null || admitted!.Contains(target.Argument))
                    .Select(target => (target.Type, target.TypeId));
            })
            .Concat(_unboundedTargets);
    }

    private static bool IsClosedRuntimeAssignableTarget(
        ITypeDefinitionResolver typeDefinitions,
        CliTypeIdentity type)
    {
        if (type.ContainsGenericParameters)
        {
            return false;
        }

        if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            return true;
        }

        if (type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
        {
            return false;
        }

        var definition = typeDefinitions.ResolveTypeIdentity(type);
        return definition.IsInterface ||
               type.Shape == CliTypeShape.GenericInstantiation &&
               definition.GenericParameterVariances.Any(variance =>
                   variance != CliGenericVariance.Invariant);
    }
}
