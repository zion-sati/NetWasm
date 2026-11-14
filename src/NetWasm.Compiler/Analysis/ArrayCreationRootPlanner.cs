using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A root-planning Strategy. Inputs are program-origin element seeds, never this
// planner's generated array roots. The caller owns demand and seed provenance.
internal sealed class ArrayCreationRootPlanner(ITypeDefinitionResolver definitions) : IArrayCreationRootPlanner
{
    private readonly ITypeDefinitionResolver _definitions = definitions ??
        throw new ArgumentNullException(nameof(definitions));

    public RuntimeIntrinsicTypeRootPlan Plan(ImmutableArray<CliTypeIdentity> elementSeeds) =>
        Plan(elementSeeds, element => [CliTypeIdentity.SzArray(element)]);

    public RuntimeIntrinsicTypeRootPlan Plan(
        ImmutableArray<CliTypeIdentity> elementSeeds,
        ImmutableArray<int> ranks)
    {
        if (ranks.IsDefault || ranks.Any(rank => rank < 2))
        {
            throw new ArgumentException("Rectangular ranks must be initialized and at least two.", nameof(ranks));
        }
        return Plan(elementSeeds, element => ranks.Select(rank => CliTypeIdentity.Array(element, rank)));
    }

    private RuntimeIntrinsicTypeRootPlan Plan(
        ImmutableArray<CliTypeIdentity> elementSeeds,
        Func<CliTypeIdentity, IEnumerable<CliTypeIdentity>> arrays)
    {
        if (elementSeeds.IsDefault)
        {
            throw new ArgumentException("Element seeds must be initialized.", nameof(elementSeeds));
        }
        var runtime = new HashSet<CliTypeIdentity>();
        var allocated = new HashSet<CliTypeIdentity>();
        foreach (var element in elementSeeds.Distinct())
        {
            if (!CanAllocate(element)) continue;
            runtime.Add(element);
            foreach (var array in arrays(element))
            {
                runtime.Add(array);
                allocated.Add(array);
            }
        }
        if (allocated.Count == 0) return RuntimeIntrinsicTypeRootPlan.Empty;
        return new(
            [.. runtime.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            [.. allocated.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)]);
    }

    private bool CanAllocate(CliTypeIdentity element)
    {
        if (element.ContainsGenericParameters || element.StackKind == CliValueKind.Void)
            return false;
        switch (element.Shape)
        {
            case CliTypeShape.Primitive:
            case CliTypeShape.UnmanagedPointer:
            case CliTypeShape.FunctionPointer:
            case CliTypeShape.SzArray:
            case CliTypeShape.Array:
                return true;
            case CliTypeShape.Named:
            case CliTypeShape.GenericInstantiation:
                var definition = _definitions.ResolveTypeIdentity(element);
                return !definition.IsByRefLike &&
                    (element.Shape == CliTypeShape.GenericInstantiation || definition.GenericArity == 0);
            default:
                return false;
        }
    }
}
