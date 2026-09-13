using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicTypeRootPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    INullableTypeResolver nullableTypes) : IRuntimeIntrinsicTypeRootPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));

    public ImmutableArray<CliTypeIdentity> Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<CliTypeIdentity> constructedTypes)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        ArgumentNullException.ThrowIfNull(constructedTypes);

        if (!reachableMethods.Any(method =>
                _intrinsics.TryGetIntrinsic(method.Definition.Key, out var intrinsic) &&
                intrinsic == RuntimeIntrinsic.NullableGetUnderlyingType))
        {
            return [];
        }

        return [.. constructedTypes
            .Select(_nullableTypes.Resolve)
            .OfType<CliTypeIdentity>()
            .Distinct()
            .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)];
    }
}
