using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodBodyEmitter(
    IManagedMethodEmitter managedMethods,
    IManagedMethodSequenceEmitter sequences,
    IStackTraceMethodIdProvider stackTraceMethodIds,
    IManagedHeapSampler heapSampler,
    IAddressTakenScalarPromotionPlanner addressTakenScalarPromotions,
    IAddressTakenAggregatePromotionPlanner addressTakenAggregatePromotions,
    IManagedByRefAggregatePromotionPlanner managedByRefAggregatePromotions,
    IManagedMethodValuePlanBuilder managedValues) :
    IManagedMethodBodyEmitter
{
    private readonly IManagedHeapSampler _heapSampler = heapSampler ??
        throw new ArgumentNullException(nameof(heapSampler));
    private readonly IAddressTakenScalarPromotionPlanner _addressTakenScalarPromotions =
        addressTakenScalarPromotions ??
        throw new ArgumentNullException(nameof(addressTakenScalarPromotions));
    private readonly IAddressTakenAggregatePromotionPlanner _addressTakenAggregatePromotions =
        addressTakenAggregatePromotions ??
        throw new ArgumentNullException(nameof(addressTakenAggregatePromotions));
    private readonly IManagedByRefAggregatePromotionPlanner _managedByRefAggregatePromotions =
        managedByRefAggregatePromotions ??
        throw new ArgumentNullException(nameof(managedByRefAggregatePromotions));
    private readonly IManagedMethodValuePlanBuilder _managedValues = managedValues ??
        throw new ArgumentNullException(nameof(managedValues));

    public ManagedMethodBodyEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        MethodInstanceModel? methodInstance = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var started = Stopwatch.GetTimestamp();
        var memoryBefore = target.CollectManagedMethodMemoryMetrics
            ? _heapSampler.Sample()
            : (long?)null;
        ManagedMethodSequenceEmission? sequenceEmission = null;
        var stackTraceMethodId = stackTraceMethodIds.GetId(
            target.StackTraceMethods,
            method,
            methodInstance);
        var scalarPromotions = _addressTakenScalarPromotions.Plan(new(
            structured,
            callerIdentity,
            target.ManagedCallSites,
            target.ManagedByRefEffects,
            Enabled: false));
        var aggregatePromotions = _addressTakenAggregatePromotions.Plan(new(
            structured,
            callerIdentity,
            target.ManagedCallSites,
            target.ManagedByRefEffects,
            target.EnableAddressTakenAggregatePromotion));
        var managedByRefPromotions = _managedByRefAggregatePromotions.Plan(new(
            structured,
            callerIdentity,
            target.ManagedCallSites,
            target.EnableAddressTakenAggregatePromotion));
        var reservedStorages = aggregatePromotions.PromotedLocals.Keys
            .Select(index => new ManagedAggregateStorageId(
                ManagedAggregateStorageKind.Local,
                index))
            .Concat(managedByRefPromotions.PromotedParameters.Keys.Select(index =>
                new ManagedAggregateStorageId(
                    ManagedAggregateStorageKind.Argument,
                    index)))
            .ToImmutableHashSet();
        var managedValuePlan = _managedValues.Build(new(
            structured,
            rootMap,
            target.ManagedByRefEffects,
            reservedStorages,
            target.EnableAddressTakenAggregatePromotion)
        {
            References = target.EnableAggregateReferenceLanes,
        });
        var emission = managedMethods.Emit(
            method,
            callerIdentity,
            structured,
            managedValuePlan.EmissionRootMap,
            methodInstance,
            stackTraceMethodId,
            target.RuntimeImportSelection,
            (code, emittedMethod, context) =>
                sequenceEmission = sequences.Emit(
                    code,
                    emittedMethod,
                    emittedMethod.Body,
                    context,
                    target,
                    functionIndices),
            target.NativeImports,
            target.MemberExecution,
            target.ManagedByRefEffects.Methods.TryGetValue(
                callerIdentity.CanonicalName,
                out var byRefEffects)
                ? byRefEffects
                : null,
            scalarPromotions,
            aggregatePromotions,
            managedByRefPromotions,
            managedValuePlan.Aggregates,
            target.EnableBranchValueFramePlacement,
            target.EnableArrayMetadataCaching,
            target.EnableScalarCallPreservation,
            target.HoistArrayNullChecks,
            target.LoopRotation,
            target.EliminateArrayStoreChecks,
            target.FunctionIndices.ManagedThrowFunctionIndex,
            target.InlineSmallBlockOperations);
        return new ManagedMethodBodyEmission(
            emission.Body,
            emission.WasmInstructionCount,
            Stopwatch.GetElapsedTime(started).Ticks,
            memoryBefore is null
                ? null
                : Math.Max(memoryBefore.Value, _heapSampler.Sample()),
            ManagedMethodBodyKey.Resolve(structured),
            emission.FilterEnvironment,
            sequenceEmission?.OriginalBlockEmissionCounts ?? []);
    }

}
