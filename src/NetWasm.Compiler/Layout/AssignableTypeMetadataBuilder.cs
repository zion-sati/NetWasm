using System;
using System.Buffers.Binary;
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
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IAssignableTypeMetadataBuilder
{
    private const int TypeIdSize = sizeof(int);

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

    // The runtime types filed under their own relationship key, and those without one.
    private Dictionary<string, List<(CliTypeIdentity Type, int TypeId)>>? _targetsByKey;
    private readonly List<(CliTypeIdentity Type, int TypeId)> _unboundedTargets = [];

    public AssignableTypeMetadataLayout Build(CliTypeIdentity candidate)
    {
        if (candidate.Shape is CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer ||
            candidate.ContainsGenericParameters ||
            candidate.Shape == CliTypeShape.Named &&
            typeDefinitions.ResolveTypeIdentity(candidate).GenericArity != 0)
        {
            return default;
        }

        // A candidate classifies only the runtime types filed under the keys it reaches.
        var typeIds = Targets(candidate)
            .Where(target => relationships
                .Classify(candidate, target.Type)
                .IsAssignmentCompatible)
            .Select(target => target.TypeId)
            .Distinct()
            .Order()
            .ToArray();
        if (typeIds.Length == 0)
        {
            return default;
        }

        state.Cursor = ManagedTypeLayoutCompiler.Align(state.Cursor, TypeIdSize);
        var address = state.Cursor;
        var data = new byte[typeIds.Length * TypeIdSize];
        for (var index = 0; index < typeIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * TypeIdSize, TypeIdSize),
                typeIds[index]);
        }
        state.Segments.Add(new DataSegment(address, [.. data]));
        state.Cursor += data.Length;
        return new AssignableTypeMetadataLayout(address, typeIds.Length);
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
                    filed.Add(target);
                }
                else
                {
                    _targetsByKey.Add(keys.Own, [target]);
                }
            }
        }
        var candidateKeys = relationshipKeys.Resolve(candidate);
        return candidateKeys.Unbounded
            ? _runtimeAssignableTypes
            : candidateKeys.Reachable
                .SelectMany(key => _targetsByKey.GetValueOrDefault(key) ?? [])
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
