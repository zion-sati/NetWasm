// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    // The runtime types filed under their own relationship key, each key's indexed by its targets' first type
    // argument's (or element's) key, and those without a key.
    private Dictionary<string, KeyFiling>? _targetsByKey;
    private readonly List<(CliTypeIdentity Type, int TypeId)> _unboundedTargets = [];

    public ImmutableArray<int> Build(CliTypeIdentity candidate)
    {
        if (candidate.Shape is CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer ||
            candidate.ContainsGenericParameters ||
            candidate.Shape == CliTypeShape.Named &&
            typeDefinitions.ResolveTypeIdentity(candidate).GenericArity != 0)
        {
            return [];
        }

        // A candidate classifies only the runtime types filed under the keys it reaches, each pair once. A shared
        // array's casting list holds only what every array of its shape converts to; the rest depends on its element.
        return [.. Targets(candidate)
            .Where(target => relationships.Classify(candidate, target.Type) is
                { IsAssignmentCompatible: true, RequiresElementCheck: false })
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
                    continue;
                }
                if (!_targetsByKey.TryGetValue(keys.Own, out var filing))
                {
                    _targetsByKey.Add(keys.Own, filing = new KeyFiling());
                }
                File(filing, new(target.Type, target.TypeId, keys.ArgumentKey, keys.NestedArgumentKey));
            }
        }
        var candidateKeys = relationshipKeys.Resolve(candidate);
        if (candidateKeys.Unbounded)
        {
            return _runtimeAssignableTypes;
        }
        // Instantiations of a generic definition the candidate reaches are filtered by their first type argument:
        // every array type would otherwise be classified against every IEnumerable<T>, IList<T> and
        // IReadOnlyList<T> in the program, and Newtonsoft.Json's program held over 12 GB of such pairs. An array's
        // targets under the keys whose argument is its element are filtered one level further, by what the element
        // admits of their argument's argument: every array of some IEnumerable<T> was classified against every other
        // (about 5,000 targets a candidate on Devolutions' Avalonia sample, 380 million pairs). A key's targets are
        // indexed by argument, so a candidate visits only those it admits: scanning each key's whole list filtered
        // the same 175,000 runtime types for every candidate there, 840 of 916 s.
        return candidateKeys.Reachable
            .SelectMany(key =>
            {
                if (_targetsByKey.GetValueOrDefault(key) is not { } filing)
                {
                    return [];
                }
                IReadOnlySet<string>? admitted = null;
                var filtered = candidateKeys.Arguments?.TryGetValue(key, out admitted) == true && admitted is not null;
                var nested = candidateKeys.ElementKeys?.Contains(key) == true ? candidateKeys.ElementArguments : null;
                return Admitted(filing, filtered ? admitted : null)
                    .Where(target => nested is null || target.Argument is null || target.NestedArgument is null ||
                        nested.GetValueOrDefault(target.Argument) is not { } admittedNested ||
                        admittedNested.Contains(target.NestedArgument))
                    .Select(target => (target.Type, target.TypeId));
            })
            .Concat(_unboundedTargets);
    }

    private static void File(KeyFiling filing, FiledTarget target)
    {
        filing.All.Add(target);
        if (target.Argument is null)
        {
            filing.WithoutArgument.Add(target);
        }
        else if (filing.ByArgument.TryGetValue(target.Argument, out var filed))
        {
            filed.Add(target);
        }
        else
        {
            filing.ByArgument.Add(target.Argument, [target]);
        }
    }

    // A key's every target when nothing narrows it, else those whose argument the set admits and those without one.
    private static IEnumerable<FiledTarget> Admitted(KeyFiling filing, IReadOnlySet<string>? arguments) =>
        arguments is null
            ? filing.All
            : arguments
                .SelectMany(argument => filing.ByArgument.GetValueOrDefault(argument) ?? [])
                .Concat(filing.WithoutArgument);

    // One key's targets: all of them, and by argument key with those that have none.
    private sealed class KeyFiling
    {
        public List<FiledTarget> All { get; } = [];

        public List<FiledTarget> WithoutArgument { get; } = [];

        public Dictionary<string, List<FiledTarget>> ByArgument { get; } = new(StringComparer.Ordinal);
    }

    private sealed record FiledTarget(CliTypeIdentity Type, int TypeId, string? Argument, string? NestedArgument);

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
