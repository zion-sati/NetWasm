// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ManagedMethodEmission(
    byte[] Body,
    FilterEnvironmentLayout FilterEnvironment,
    int WasmInstructionCount);

internal sealed class ManagedMethodEmitter(
    ITargetLayout layouts,
    IManagedMethodFunctionTypeResolver functionTypes,
    IValueFrameLayoutPlanner valueFrames,
    IFilterEnvironmentLayoutPlanner filterEnvironments,
    IExceptionGroupEnumerator exceptionGroups,
    IMethodFrameEntryEmitter frames,
    IGeneratedFunctionWriterFactory writers,
    IInstructionCountingWriterFactory instructionWriters,
    ITerminalBranchValueFramePlanner branchValueFrames,
    IArrayMetadataCachingPlanner arrayMetadata,
    IArrayLengthRunPlanner arrayLengthRuns,
    IArrayMetadataSnapshotEmitter arraySnapshots,
    IArrayStoreCheckPlanner arrayStoreChecks,
    IScalarCallLifetimePlanner scalarCallLifetimes,
    IAuthoritativeRootSourceSelector authoritativeSources,
    IAuthoritativeRootLayoutPlanner authoritativeLayouts,
    IRootClearElisionPlanner rootClearElisions,
    IRootFrameEntryClearPlanner rootFrameEntryClears,
    ILazyRootFramePlanner lazyRootFrames,
    IRootValueElisionPlanner rootValueElisions,
    IValueFrameReferenceMapPlanner frameReferences,
    IRootSlotAllocator rootSlots,
    IExceptionFrameStrategySelector frameStrategies,
    ILandingPadRestorePlanner landingPadRestores,
    IStructResultScratchPlanner? structResultScratch = null,
    IStructResultClassifier? structResults = null,
    IFieldArgumentPlanner? fieldArgumentPlanner = null,
    IStructLanePlanner? structLanePlanner = null) : IManagedMethodEmitter
{
    public ManagedMethodEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        int stackTraceMethodId,
        RuntimeImportSelection runtimeImportSelection,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody,
        NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null,
        ManagedByRefMethodEffects? byRefEffects = null,
        AddressTakenScalarPromotionPlan? addressTakenScalarPromotions = null,
        AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
        ManagedByRefAggregatePromotionPlan? managedByRefAggregatePromotions = null,
        ManagedAggregateSsaPlan? managedAggregateValues = null,
        BranchPrefixDuplicationPolicy branchPrefixDuplication = BranchPrefixDuplicationPolicy.Disabled,
        bool enableArrayMetadataCaching = false,
        bool enableScalarCallPreservation = false,
        bool hoistArrayNullChecks = false,
        LoopRotationPolicy loopRotation = LoopRotationPolicy.Disabled,
        bool eliminateArrayStoreChecks = false,
        OptionalFunctionIndex managedThrowFunction = default,
        bool inlineSmallBlocks = false,
        bool inlineArrayData = false,
        bool conditionalAllocationRoots = false,
        bool reachesFilters = true)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(emitBody);

        var effectiveMethodInstance = methodInstance ?? structured.Header.MethodInstance;
        var effectiveSignature = effectiveMethodInstance?.Signature ?? method.Signature;
        var emissionStructure = structured with
        {
            Header = structured.Header with { MethodInstance = effectiveMethodInstance },
        };
        // A value returned in results, call results, and temporaries built for calls stay in lanes where they can.
        var returnShape = effectiveSignature.ReturnSignatureType.StackKind == CliValueKind.ValueType
            ? structResults?.Classify(effectiveSignature.ReturnSignatureType)
            : null;
        var lanes = structLanePlanner?.Plan(emissionStructure, rootMap, returnShape) ?? StructLanePlan.Empty;
        var returnLanes = lanes.Return;
        var callResultLanes = lanes.CallResults;
        var argumentLanes = lanes.Arguments;
        // The struct arguments passed by their fields, and those read in place from their parameters.
        var fieldArgumentPlan = fieldArgumentPlanner?.Plan(emissionStructure, method, effectiveSignature, returnShape) ??
            SequentialArguments(method, effectiveSignature, structResults);
        var argumentParameters = fieldArgumentPlan.ArgumentParameters;
        var fieldArguments = fieldArgumentPlan.Copied;
        var fieldArgumentsInPlace = fieldArgumentPlan.InPlace;
        var forwardedLoads = fieldArgumentPlan.ForwardedLoads;
        var confinedArguments = fieldArgumentPlan.Confined;
        var argumentLaneOrder = argumentLanes.Locals.Keys.Order().ToArray();
        ImmutableArray<CliValueKind> argumentLaneKinds = [.. argumentLaneOrder.SelectMany(local => argumentLanes.Locals[local].Results)];
        var valueLayout = valueFrames.Create(
            emissionStructure,
            nativeImports,
            memberExecution,
            byRefEffects,
            addressTakenScalarPromotions,
            addressTakenAggregatePromotions,
            scalarCallLifetimes.Plan(emissionStructure, enableScalarCallPreservation),
            // A value returned in results has no buffer to live in; its lanes keep it out of the frame too.
            returnShape is null
                ? ReturnBufferLocals.Find(emissionStructure, rootMap)
                : null,
            new StructConventionStorage(lanes.Locals, callResultLanes.Calls.Keys.ToHashSet(),
                argumentLanes.WholeLoads.Keys.Concat(forwardedLoads).ToHashSet(), fieldArguments.Keys.ToHashSet(),
                fieldArgumentsInPlace.Keys.ToHashSet()));
        valueLayout = valueLayout with
        {
            ManagedByRefAggregatePromotions = managedByRefAggregatePromotions ??
                ManagedByRefAggregatePromotionPlan.Empty,
        };
        var filterEnvironment = filterEnvironments.Create(emissionStructure, valueLayout, rootMap);
        valueLayout = valueLayout with
        {
            Size = Math.Max(valueLayout.Size, filterEnvironment.Size),
        };
        // A value type returned through a buffer takes the buffer first; one returned in results takes none.
        var parameterOffset = effectiveSignature.ReturnSignatureType.StackKind == CliValueKind.ValueType &&
            structResults?.Classify(effectiveSignature.ReturnSignatureType) is null ? 1 : 0;
        var parameterCount = effectiveMethodInstance is null
            ? functionTypes.Resolve(method).Parameters.Length
            : functionTypes.Resolve(effectiveMethodInstance).Parameters.Length;
        var groups = exceptionGroups.Enumerate(emissionStructure).ToArray();
        rootMap = LaneRoots.Exclude(rootMap, rootSlots, lanes.Locals, confinedArguments);
        var placement = branchPrefixDuplication != BranchPrefixDuplicationPolicy.Disabled && filterEnvironment.Size == 0 &&
            valueLayout.ScalarCallLifetimes.CallLocals.IsEmpty
            ? branchValueFrames.Plan(emissionStructure, valueLayout, rootMap,
                copyTails: branchPrefixDuplication == BranchPrefixDuplicationPolicy.Speed)
            : null;
        // A root frame alone, placed in the arm that does not throw, is entered on every path that returns,
        // where a lazy root frame is entered only on paths that publish a root.
        if (placement is not null && valueLayout.Size == 0) placement = null;
        var allocationLayout = placement is null ? valueLayout : valueLayout with
        {
            SpilledScalarLocals = [],
            AddressTakenScalarPromotions = new(placement.IncomingScalarLocals.ToImmutableDictionary(
                index => index, index => emissionStructure.Header.LocalSignatureTypes[index]), []),
        };
        var arrayPlan = arrayMetadata.Plan(emissionStructure, enableArrayMetadataCaching, hoistArrayNullChecks);
        var lengthRuns = arrayLengthRuns.Plan(emissionStructure, arrayPlan, enableArrayMetadataCaching);
        // A collection reads the frame storage its map covers, so no safepoint
        // publishes it; a frame placed on terminal branches keeps publishing.
        var references = placement is null
            ? frameReferences.Plan(emissionStructure.Header, allocationLayout, filterEnvironment, managedAggregateValues)
            : ValueFrameReferenceMap.Empty;
        rootMap = references.Exclude(rootMap, rootSlots);
        // The protected blocks whose strategy keeps their marks in locals.
        var markedGroups = groups.Where(id => frameStrategies.Select(emissionStructure.ExceptionGroups[id], reachesFilters).KeepsMarks)
            .ToHashSet();
        // Which variables are authoritative is weighed on the context the method would have with none, the frame
        // placed as it will be.
        var structResultLocals = structResultScratch?.Plan(emissionStructure.Header, memberExecution) ?? [];
        var unselected = MethodEmissionContextFactory.Create(emissionStructure.Header, rootMap, parameterCount,
            parameterOffset, groups, allocationLayout, filterEnvironment, callerIdentity, managedAggregateValues, arrayPlan,
            lengthRuns, markedGroups, structResultLocals, fieldArguments.Count, argumentLaneKinds).Context with
        {
            ConditionalAllocationRoots = conditionalAllocationRoots,
            ReachesFilters = reachesFilters,
            TerminalBranchValueFramePlacement = placement is null ? null : new(placement, valueLayout),
        };
        var authoritative = authoritativeLayouts.Plan(rootMap,
            authoritativeSources.Select(emissionStructure, unselected, arrayPlan));
        rootMap = authoritative.RootMap;
        // A collection reads every capture where it lives when the value frame's map covers them, so no
        // filter root frame copies them; funclets publish into this method's root frame by its final slots.
        if (filterEnvironment.RootSlotCount != 0 && references.CoversCaptures(filterEnvironment))
            filterEnvironment = filterEnvironment.WithoutRootFrame();
        if (!filterEnvironment.IsEmpty) filterEnvironment = filterEnvironment with { ParentRootMap = rootMap };
        var emissionLayout = MethodEmissionContextFactory.Create(
            emissionStructure.Header,
            rootMap,
            parameterCount,
            parameterOffset,
            groups,
            allocationLayout,
            filterEnvironment,
            callerIdentity,
            managedAggregateValues,
            arrayPlan,
            lengthRuns,
            markedGroups,
            structResultLocals,
            fieldArguments.Count,
            argumentLaneKinds);
        var laneLocalsByTemporary = ImmutableDictionary.CreateBuilder<int, ImmutableArray<int>>();
        {
            var at = 0;
            foreach (var local in argumentLaneOrder)
            {
                var width = argumentLanes.Locals[local].Results.Length;
                laneLocalsByTemporary[local] = emissionLayout.Context.ArgumentLaneFlat.Slice(at, width);
                at += width;
            }
        }
        // A field argument's local is the address of its frame copy, which the entry fills.
        var argumentLocals = argumentParameters.ToArray();
        {
            var address = 0;
            foreach (var argument in fieldArguments.Keys.Order()) argumentLocals[argument] = emissionLayout.Context.FieldArgumentAddressLocals[address++];
            foreach (var (argument, fieldArgument) in fieldArgumentsInPlace) argumentLocals[argument] = fieldArgument.FirstParameter;
        }
        var context = emissionLayout.Context with
        {
            ArgumentLocals = [.. argumentLocals],
            FieldArguments = fieldArguments,
            FieldArgumentAddresses = fieldArgumentPlan.FieldAddresses,
            FieldArgumentLoads = fieldArgumentPlan.FieldLoads,
            ForwardedFieldArguments = fieldArgumentPlan.Forwarded,
            ForwardedArgumentLoads = forwardedLoads,
            ReturnedFieldArguments = fieldArgumentPlan.Returned,
            InPlaceFieldArguments = fieldArgumentsInPlace,
            ArgumentLanes = argumentLanes,
            ArgumentLaneLocals = laneLocalsByTemporary.ToImmutable(),
            ReturnLanes = returnLanes,
            CallResultLanes = callResultLanes,
            StackTraceMethodId = stackTraceMethodId,
            RuntimeImportSelection = runtimeImportSelection,
            ManagedThrowFunction = managedThrowFunction,
            AuthoritativeRootSlots = authoritative.Slots,
            ValueFrameReferences = references,
            LoopRotation = loopRotation,
            InlineSmallBlocks = inlineSmallBlocks,
            InlineArrayData = inlineArrayData,
            ConditionalAllocationRoots = conditionalAllocationRoots,
            ReachesFilters = reachesFilters,
            ExactArrayStores = arrayStoreChecks.Plan(emissionStructure, eliminateArrayStoreChecks),
        };
        if (placement is not null)
        {
            context = context with
            {
                ValueLayout = allocationLayout with { Size = 0, LocalOffsets = [], TemporaryOffsets = [], ArgumentOffsets = [] },
                TerminalBranchValueFramePlacement = new(placement, valueLayout)
                {
                    FramedReferences = frameReferences.Plan(
                        emissionStructure.Header, valueLayout, filterEnvironment, managedAggregateValues),
                },
            };
        }
        // A lazy frame is zeroed where it is entered, so the clear elisions are planned as for a zeroed frame.
        context = context with { LazyRootFrame = lazyRootFrames.Plan(emissionStructure, context) };
        context = context with { LandingPadRestores = landingPadRestores.Plan(emissionStructure, context) };
        context = context with
        {
            RootFrameStartsZeroed = context.LazyRootFrame is not null || rootFrameEntryClears.Plan(emissionStructure, context),
        };
        context = context with { RootClearElisions = rootClearElisions.Plan(emissionStructure, context) };
        context = context with { RootValueElisions = rootValueElisions.Plan(emissionStructure, context) };
        if (placement is { DefersRootFrame: true } && context.TerminalBranchValueFramePlacement is { } branchFrame)
        {
            // Nothing outside the framed arm holds a root, so that arm enters the root frame, with
            // the elisions planned over the whole method; elsewhere no slot exists to publish into.
            context = context with
            {
                TerminalBranchValueFramePlacement = branchFrame with
                {
                    FramedRootMap = context.RootMap,
                    FramedAuthoritativeRootSlots = context.AuthoritativeRootSlots,
                },
                RootMap = context.RootMap with { Slots = [] },
                AuthoritativeRootSlots = [],
            };
        }
        var output = writers.Create();
        var code = instructionWriters.Create(output.Instructions);
        WasmLocalDeclarationWriter.Write(
            output.Bytes,
            emissionLayout.LocalTypes,
            layouts.Target);
        frames.Emit(code, emissionStructure.Header, context);
        arraySnapshots.Emit(code, context);
        // An exception leaves this method's frames in place; whatever catches it
        // rewinds the frame arenas to where they stood when its region began.
        emitBody(code, emissionStructure, context);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return new ManagedMethodEmission(
            output.Snapshots.Read(),
            filterEnvironment,
            code.InstructionCount);
    }

    // Without struct arguments passed by their fields: each argument is one parameter, after a return buffer's.
    private static FieldArgumentPlan SequentialArguments(MethodDefinitionModel method, MethodSignatureModel signature,
        IStructResultClassifier? structResults)
    {
        var first = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType &&
            structResults?.Classify(signature.ReturnSignatureType) is null ? 1 : 0;
        var count = signature.ParameterSignatureTypes.Length + (method.IsStatic ? 0 : 1);
        return new([.. Enumerable.Range(first, count)], ImmutableDictionary<int, FieldArgument>.Empty,
            ImmutableDictionary<int, FieldArgument>.Empty, [], ImmutableDictionary<int, (int, CliValueKind)>.Empty,
            ImmutableDictionary<(int, int), int>.Empty, [], ImmutableDictionary<int, int>.Empty, []);
    }
}
