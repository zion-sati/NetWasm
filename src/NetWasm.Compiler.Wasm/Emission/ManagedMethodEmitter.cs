// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
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
    IRootValueElisionPlanner rootValueElisions,
    IValueFrameReferenceMapPlanner frameReferences,
    IRootSlotAllocator rootSlots) : IManagedMethodEmitter
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
        bool conditionalAllocationRoots = false)
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
        var valueLayout = valueFrames.Create(
            emissionStructure,
            nativeImports,
            memberExecution,
            byRefEffects,
            addressTakenScalarPromotions,
            addressTakenAggregatePromotions,
            scalarCallLifetimes.Plan(emissionStructure, enableScalarCallPreservation));
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
        var parameterOffset = effectiveSignature.ReturnSignatureType.StackKind ==
            CliValueKind.ValueType ? 1 : 0;
        var parameterCount = effectiveMethodInstance is null
            ? functionTypes.Resolve(method).Parameters.Length
            : functionTypes.Resolve(effectiveMethodInstance).Parameters.Length;
        var groups = exceptionGroups.Enumerate(emissionStructure).ToArray();
        var placement = branchPrefixDuplication != BranchPrefixDuplicationPolicy.Disabled && filterEnvironment.Size == 0 &&
            valueLayout.ScalarCallLifetimes.CallLocals.IsEmpty
            ? branchValueFrames.Plan(emissionStructure, valueLayout, rootMap,
                copyTails: branchPrefixDuplication == BranchPrefixDuplicationPolicy.Speed)
            : null;
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
        var authoritative = authoritativeLayouts.Plan(rootMap,
            authoritativeSources.Select(rootMap, allocationLayout, filterEnvironment, arrayPlan));
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
            lengthRuns);
        var context = emissionLayout.Context with
        {
            StackTraceMethodId = stackTraceMethodId,
            RuntimeImportSelection = runtimeImportSelection,
            ManagedThrowFunction = managedThrowFunction,
            AuthoritativeRootSlots = authoritative.Slots,
            ValueFrameReferences = references,
            LoopRotation = loopRotation,
            InlineSmallBlocks = inlineSmallBlocks,
            InlineArrayData = inlineArrayData,
            ConditionalAllocationRoots = conditionalAllocationRoots,
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
        context = context with { RootFrameStartsZeroed = rootFrameEntryClears.Plan(emissionStructure, context) };
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

}
