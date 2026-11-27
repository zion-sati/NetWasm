// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class RootPublicationEmitter(
    ITargetLayout layouts,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots,
    IValueFrameAddressEmitter addresses,
    IStackStorageAnchorValueEmitter anchors,
    ILazyRootFrameEmitter lazyRootFrames) : IRootPublicationEmitter
{
    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, bool constructorCall = false)
    {
        var context = request.Context;
        if (!context.RootMap.Safepoints.TryGetValue(
                request.Instruction.Offset,
                out var safepoint))
        {
            return;
        }
        var roots = constructorCall
            ? safepoint.ConstructorCallRoots
            : safepoint.Roots;
        if (context.RootMap.SlotCount == 0 && roots.IsEmpty)
        {
            return;
        }
        if (constructorCall && roots.IsEmpty)
        {
            filterEnvironmentRoots.Emit(code, context);
            return;
        }

        var rootsBySlot = new RootSource?[context.RootMap.SlotCount];
        foreach (var source in roots)
        {
            if (!context.RootMap.Slots.TryGetValue(source, out var slot) ||
                slot < 0 || slot >= rootsBySlot.Length)
            {
                throw new InvalidOperationException(
                    $"Root source {source} has no valid physical slot.");
            }
            if (rootsBySlot[slot] is not null)
            {
                throw new InvalidOperationException(
                    $"Multiple live root sources use physical slot {slot}.");
            }
            rootsBySlot[slot] = source;
        }

        filterEnvironmentRoots.Emit(code, context);
        context.RootClearElisions.TryGetValue((request.Instruction.Offset, constructorCall), out var knownZero);
        context.RootValueElisions.TryGetValue(request.Instruction.Offset, out var unchanged);
        // A deferrable root whose slot holds null stays unpublished, like a dead slot that holds null.
        var deferred = constructorCall ? [] : DeferredRoots.Of(context.RootMap, safepoint, knownZero);
        bool Stored(int slot) =>
            !(!constructorCall && rootsBySlot[slot] is not null && unchanged?.Contains(slot) == true) &&
            !((rootsBySlot[slot] is null || deferred.Contains(rootsBySlot[slot]!.Value)) && knownZero?.Contains(slot) == true) &&
            !(rootsBySlot[slot] is { } authoritative && context.AuthoritativeRootSlots.ContainsKey(authoritative));
        // A lazy frame: a safepoint that publishes a root enters it first; one that only clears slots clears them only
        // in a frame the method has entered.
        // A live root needs the frame even where its slot is not written here (an authoritative slot, a value the slot
        // still holds), so a safepoint with one enters it.
        var guarded = false;
        if (context.LazyRootFrame is { } lazy && !lazy.Entered.Contains(request.Instruction.Offset))
        {
            if (roots.Any(source => !deferred.Contains(source)))
            {
                // The slots stored below are written before anything can collect, so the entry need not zero them.
                lazyRootFrames.Emit(code, LazyRootFrameStep.Ensure, context,
                    publishedSlots: Enumerable.Range(0, rootsBySlot.Length).Where(Stored).ToHashSet());
            }
            else if (!Enumerable.Range(0, rootsBySlot.Length).Any(Stored))
            {
                return;
            }
            else
            {
                lazyRootFrames.Emit(code, LazyRootFrameStep.Test, context);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                guarded = true;
            }
        }
        for (var rootSlot = 0; rootSlot < rootsBySlot.Length; rootSlot++)
        {
            if (!Stored(rootSlot)) continue;
            // The frame is a live allocation, so the slot offset cannot wrap and
            // the store carries it instead of an address addition.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.RootFrame))));

            if (rootsBySlot[rootSlot] is { } promoted && TryGetPromotedLane(context, promoted, out var lane))
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)lane)));
            }
            else if (rootsBySlot[rootSlot] is { } source)
            {
                // An offset source is a reference field of a value held here: in a local's or an
                // argument's storage, or in an evaluation-stack value's temporary, never null.
                EmitSourceValue(code, context, source);
                if (source.IsIndirect || source.Kind == RootSourceKind.Local &&
                    !context.FilterEnvironment.Captures.ContainsKey(new(false, source.Index)) &&
                    context.ValueLayout.SpilledScalarLocals.Contains(source.Index))
                {
                    ManagedMemoryEmitter.EmitLoadBySize(
                        code,
                        layouts.Target,
                        source.IsIndirect ? source.ByteOffset : 0,
                        layouts.Target.ObjectReferenceSize);
                }
            }
            else
            {
                EmitAddressConstant(code, 0);
            }

            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                checked(rootSlot * layouts.Target.ObjectReferenceSize),
                layouts.Target.ObjectReferenceSize);
        }
        if (guarded) code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // A reference or byref field of a promoted aggregate lives in its lane
    // local; the value frame holds it only around materializations.
    private static bool TryGetPromotedLane(MethodEmissionContext context, RootSource source, out int local)
    {
        local = -1;
        // A reference field of an argument read in place is in the parameter that holds that field.
        if (source is { Kind: RootSourceKind.Argument, IsIndirect: true } &&
            context.InPlaceFieldArguments.TryGetValue(source.Index, out var inPlace))
        {
            for (var lane = 0; lane < inPlace.Shape.Fields.Length; lane++)
            {
                if (inPlace.Shape.Fields[lane].Offset != source.ByteOffset) continue;
                local = inPlace.FirstParameter + lane;
                return true;
            }
            throw new InvalidOperationException($"Argument {source.Index} in place has no field at {source.ByteOffset}.");
        }
        var kind = source.Kind switch
        {
            RootSourceKind.Argument => ManagedAggregateStorageKind.Argument,
            RootSourceKind.Local => ManagedAggregateStorageKind.Local,
            _ => (ManagedAggregateStorageKind?)null,
        };
        return source.IsIndirect && kind is { } storage &&
            context.ManagedAggregateValues.LaneLocals.TryGetValue(
                new(new(storage, source.Index), source.ByteOffset),
                out local);
    }

    private void EmitSourceValue(IWasmInstructionWriter code, MethodEmissionContext context, RootSource source)
    {
        if (source.Kind == RootSourceKind.StackStorageAnchor)
        {
            anchors.Emit(code, context, source.Index);
            return;
        }
        CapturedSlot? slot = source.Kind switch
        {
            RootSourceKind.Argument or RootSourceKind.ManagedAddressArgument => new(true, source.Index),
            RootSourceKind.Local or RootSourceKind.ManagedAddressLocal => new(false, source.Index),
            _ => null,
        };
        if (slot is { } captured && context.FilterEnvironment.Captures.TryGetValue(captured, out var capture))
        {
            addresses.Emit(code, context, capture.Offset);
            if (capture.Type.StackKind != CliValueKind.ValueType)
                ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, 0, layouts.Target.AddressSize);
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)GetSourceLocal(context, source)))));
    }

    private int GetSourceLocal(MethodEmissionContext context, RootSource source) =>
        source.Kind switch
        {
            RootSourceKind.Argument => context.ArgumentLocal(source.Index),
            RootSourceKind.Local => context.LocalBase + source.Index,
            RootSourceKind.EvaluationStack => GetStackLocal(
                context,
                source.Index,
                source.IsIndirect
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference),
            RootSourceKind.ManagedAddressArgument =>
                context.ArgumentLocal(source.Index),
            RootSourceKind.ManagedAddressLocal => context.LocalBase + source.Index,
            RootSourceKind.ManagedAddressEvaluationStack => GetStackLocal(
                context,
                source.Index,
                CliValueKind.ManagedAddress),
            RootSourceKind.AllocationTemporary => context.ObjectTemporary,
            _ => throw new InvalidOperationException($"Unknown root source {source.Kind}."),
        };

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
