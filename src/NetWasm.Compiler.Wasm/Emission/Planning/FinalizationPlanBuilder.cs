using System.Collections.Generic;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record FinalizationPlan(
    IReadOnlySet<int> FinalizableTypeIds,
    bool UsesExplicitFinalizationApi)
{
    public bool RequiresRuntime => FinalizableTypeIds.Count != 0;

    public bool IsFinalizable(int typeId) => FinalizableTypeIds.Contains(typeId);
}

internal interface IFinalizationPlanBuilder
{
    FinalizationPlan Build(ImmutableArray<StructuredMethodEmission> methodEmissions);
}

internal sealed class FinalizationPlanBuilder(
    ITypeDescriptorSource descriptors,
    IRuntimeIntrinsicRegistry intrinsics) : IFinalizationPlanBuilder
{
    private readonly ITypeDescriptorSource _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public FinalizationPlan Build(
        ImmutableArray<StructuredMethodEmission> methodEmissions)
    {
        if (methodEmissions.IsDefault)
        {
            throw new ArgumentException(
                "Method emissions must be initialized.",
                nameof(methodEmissions));
        }

        var finalizableTypeIds = _descriptors.TypeDescriptors
            .Where(static descriptor => descriptor.Finalizer is not null)
            .Select(static descriptor => descriptor.TypeId)
            .Concat(_descriptors.ConstructedTypeDescriptors
                .Where(static descriptor => descriptor.Finalizer is not null)
                .Select(static descriptor => descriptor.TypeId))
            .ToImmutableHashSet();
        var usesExplicitFinalizationApi = methodEmissions
            .SelectMany(static emission => emission.Method.Header.Instructions)
            .Where(static instruction =>
                instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
            .Select(static instruction => CilOperands.MethodKey(instruction.Operand))
            .Any(IsStandaloneFinalizationApi);

        return new(finalizableTypeIds, usesExplicitFinalizationApi);
    }

    private bool IsStandaloneFinalizationApi(EntityKey method) =>
        _intrinsics.TryGetIntrinsic(method, out var intrinsic) &&
        intrinsic is RuntimeIntrinsic.ReRegisterForFinalize or
            RuntimeIntrinsic.GcWaitForPendingFinalizers;
}
