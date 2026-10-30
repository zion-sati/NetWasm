using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class MethodEmissionContextFactory
{
    public static MethodEmissionLayout Create(
        StructuredMethodHeader header,
        MethodRootMap rootMap,
        int parameterCount,
        int parameterOffset,
        IReadOnlyList<StructuredExceptionGroupId> exceptionGroups,
        ValueFrameLayout valueLayout,
        FilterEnvironmentLayout filterEnvironment,
        ManagedMethodIdentity callerIdentity,
        ManagedAggregateSsaPlan? managedAggregateValues = null,
        ArrayMetadataCachingPlan? arrayMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterOffset);
        ArgumentNullException.ThrowIfNull(exceptionGroups);
        ArgumentNullException.ThrowIfNull(valueLayout);
        ArgumentNullException.ThrowIfNull(filterEnvironment);

        var localBase = parameterCount;
        var stackBase = checked(localBase + header.Locals.Length);
        var stackLocals = WasmLocalLayoutPlanner.CreateEvaluationStack(
            stackBase,
            header.MaxStack);
        var objectTemporary = stackLocals.End;
        var rootFrame = checked(objectTemporary + 1);
        var exceptionTemporary = checked(rootFrame + 1);
        var exceptionFrameLocals = new Dictionary<StructuredExceptionGroupId, int>();
        var exceptionContinuationLocals = new Dictionary<StructuredExceptionGroupId, int>();
        var exceptionRootSlots = new Dictionary<StructuredExceptionGroupId, int>();
        for (var index = 0; index < exceptionGroups.Count; index++)
        {
            exceptionRootSlots.Add(exceptionGroups[index], checked(rootMap.SlotCount + index));
            exceptionFrameLocals.Add(
                exceptionGroups[index],
                checked(exceptionTemporary + 1 + index));
            exceptionContinuationLocals.Add(
                exceptionGroups[index],
                checked(exceptionTemporary + 1 + exceptionGroups.Count + index));
        }

        var valueFrame = checked(exceptionTemporary + 1 + exceptionGroups.Count * 2);
        var filterRootFrame = checked(valueFrame + 1);
        var interopDescriptor = checked(filterRootFrame + 1);
        var interopResult = checked(interopDescriptor + 1);
        var interopHandle = checked(interopResult + 1);
        var numericTemporaryI4 = checked(interopHandle + 1);
        var numericTemporaryI8 = checked(numericTemporaryI4 + 1);
        var dispatcherProgramCounter = checked(numericTemporaryI8 + 1);
        var localTypes = WasmLocalLayoutPlanner.CreateMethodLocals(
            header,
            exceptionGroups.Count,
            valueLayout.SpilledScalarLocals);
        var copyDestination = checked(parameterCount + localTypes.Length);
        localTypes = localTypes.Add(CliValueKind.ManagedAddress);
        var copySource = checked(parameterCount + localTypes.Length);
        localTypes = localTypes.Add(CliValueKind.ManagedAddress);
        var anchorLocals = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < rootMap.AnchorPlan.Anchors.Length; index++)
        {
            if (filterEnvironment.AnchorOffsets.ContainsKey(index)) continue;
            anchorLocals.Add(index, checked(parameterCount + localTypes.Length));
            localTypes = localTypes.Add(CliValueKind.ManagedAddress);
        }
        var aggregateLocals = ImmutableDictionary.CreateBuilder<
            int,
            TransientAggregateScalarLocals>();
        foreach (var producer in valueLayout.TransientAggregatePromotions.Producers
                     .OrderBy(producer => producer.Key))
        {
            var first = checked(parameterCount + localTypes.Length);
            localTypes = localTypes.Add(producer.Value.StackKind);
            var second = checked(parameterCount + localTypes.Length);
            localTypes = localTypes.Add(producer.Value.StackKind);
            aggregateLocals.Add(producer.Key, new(first, second));
        }
        var scalarShadowLocals = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var local in valueLayout.AddressTakenScalarPromotions.PromotedLocals.Keys
                     .Order())
        {
            scalarShadowLocals.Add(local, checked(parameterCount + localTypes.Length));
            localTypes = localTypes.Add(CliValueKind.ManagedAddress);
        }
        var aggregateStorageLocals = ImmutableDictionary.CreateBuilder<
            int,
            ImmutableArray<int>>();
        foreach (var local in valueLayout.AddressTakenAggregatePromotions.PromotedLocals
                     .OrderBy(pair => pair.Key))
        {
            var storageLocals = ImmutableArray.CreateBuilder<int>(
                local.Value.StorageSlots.Length);
            foreach (var storage in local.Value.StorageSlots)
            {
                storageLocals.Add(checked(parameterCount + localTypes.Length));
                localTypes = localTypes.Add(storage.StackKind);
            }
            aggregateStorageLocals.Add(local.Key, storageLocals.MoveToImmutable());
        }
        var managedByRefStorageLocals = ImmutableDictionary.CreateBuilder<
            int,
            ImmutableArray<int>>();
        foreach (var parameter in valueLayout.ManagedByRefAggregatePromotions
                     .PromotedParameters.OrderBy(pair => pair.Key))
        {
            var storageLocals = ImmutableArray.CreateBuilder<int>(
                parameter.Value.StorageSlots.Length);
            foreach (var storage in parameter.Value.StorageSlots)
            {
                storageLocals.Add(checked(parameterCount + localTypes.Length));
                localTypes = localTypes.Add(storage.StackKind);
            }
            managedByRefStorageLocals.Add(
                parameter.Key,
                storageLocals.MoveToImmutable());
        }
        var aggregateValuePlan = managedAggregateValues ?? ManagedAggregateSsaPlan.Empty;
        var aggregateValueLocals = ImmutableDictionary.CreateBuilder<
            ManagedAggregateSsaLane,
            int>();
        foreach (var lane in aggregateValuePlan.Lanes
                     .OrderBy(lane => lane.Storage.Kind)
                     .ThenBy(lane => lane.Storage.Index)
                     .ThenBy(lane => lane.ByteOffset))
        {
            aggregateValueLocals.Add(
                lane,
                checked(parameterCount + localTypes.Length));
            localTypes = localTypes.Add(aggregateValuePlan.Storages[lane.Storage].Lane(lane.ByteOffset).Kind);
        }
        var arrayPlan = arrayMetadata ?? ArrayMetadataCachingPlan.Empty;
        var arrayLocals = ImmutableDictionary.CreateBuilder<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>();
        foreach (var slot in arrayPlan.Slots)
        {
            var length = checked(parameterCount + localTypes.Length);
            localTypes = localTypes.Add(CliValueKind.I4);
            var data = checked(parameterCount + localTypes.Length);
            localTypes = localTypes.Add(CliValueKind.ManagedAddress);
            arrayLocals.Add(slot, new(length, data));
        }
        var context = new MethodEmissionContext(
            rootMap,
            localBase,
            stackBase,
            stackLocals,
            objectTemporary,
            rootFrame,
            exceptionTemporary,
            exceptionFrameLocals,
            exceptionContinuationLocals,
            valueFrame,
            valueLayout,
            filterRootFrame,
            filterEnvironment,
            parameterOffset,
            interopDescriptor,
            interopResult,
            interopHandle,
            numericTemporaryI4,
            numericTemporaryI8,
            dispatcherProgramCounter,
            CallerIdentity: callerIdentity)
        {
            ArrayMetadata = new(arrayPlan, arrayLocals.ToImmutable()),
            CopyDestinationTemporary = copyDestination,
            CopySourceTemporary = copySource,
            ExceptionRootSlots = exceptionRootSlots.ToImmutableDictionary(),
            AnchorLocals = anchorLocals.ToImmutable(),
            TransientAggregatePromotions = new(
                valueLayout.TransientAggregatePromotions,
                aggregateLocals.ToImmutable()),
            AddressTakenScalarPromotions = new(
                valueLayout.AddressTakenScalarPromotions,
                scalarShadowLocals.ToImmutable()),
            AddressTakenAggregatePromotions = new(
                valueLayout.AddressTakenAggregatePromotions,
                aggregateStorageLocals.ToImmutable()),
            ManagedByRefAggregatePromotions = new(
                valueLayout.ManagedByRefAggregatePromotions,
                managedByRefStorageLocals.ToImmutable()),
            ManagedAggregateValues = new(
                aggregateValuePlan,
                aggregateValueLocals.ToImmutable()),
        };
        return new MethodEmissionLayout(context, localTypes);
    }
}
