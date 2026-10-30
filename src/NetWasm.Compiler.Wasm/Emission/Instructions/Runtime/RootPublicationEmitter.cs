using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class RootPublicationEmitter(
    ITargetLayout layouts,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots,
    IValueFrameAddressEmitter addresses,
    IStackStorageAnchorValueEmitter anchors) : IRootPublicationEmitter
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
        for (var rootSlot = 0; rootSlot < rootsBySlot.Length; rootSlot++)
        {
            if (!constructorCall && rootsBySlot[rootSlot] is not null &&
                context.RootValueElisions.TryGetValue(request.Instruction.Offset, out var unchanged) &&
                unchanged.Contains(rootSlot)) continue;
            if (rootsBySlot[rootSlot] is null && knownZero?.Contains(rootSlot) == true) continue;
            if (rootsBySlot[rootSlot] is { } authoritative &&
                context.AuthoritativeRootSlots.ContainsKey(authoritative))
                continue;
            // The frame is a live allocation, so the slot offset cannot wrap and
            // the store carries it instead of an address addition.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.RootFrame))));

            if (rootsBySlot[rootSlot] is { } promoted && TryGetPromotedLane(context, promoted, out var lane))
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)lane)));
            }
            else if (rootsBySlot[rootSlot] is { } source)
            {
                EmitSourceValue(code, context, source);
                var guarded = source.IsIndirect &&
                    (source.Kind is RootSourceKind.StackStorageAnchor or
                        RootSourceKind.ManagedAddressArgument or RootSourceKind.ManagedAddressLocal ||
                     source.Kind == RootSourceKind.EvaluationStack && roots.Contains(
                         new RootSource(RootSourceKind.ManagedAddressEvaluationStack, source.Index)));
                if (guarded)
                {
                    code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                        WasmInstructionOperand.BlockType(layouts.Target.UsesMemory64 ? (byte)0x7e : (byte)0x7f)));
                    EmitAddressConstant(code, 0);
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                    EmitSourceValue(code, context, source);
                    ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, source.ByteOffset, layouts.Target.ObjectReferenceSize);
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                }
                else if (source.IsIndirect || source.Kind == RootSourceKind.Local &&
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
    }

    // A reference or byref field of a promoted aggregate lives in its lane
    // local; the value frame holds it only around materializations.
    private static bool TryGetPromotedLane(MethodEmissionContext context, RootSource source, out int local)
    {
        local = -1;
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
            RootSourceKind.Argument => source.Index + context.ParameterOffset,
            RootSourceKind.Local => context.LocalBase + source.Index,
            RootSourceKind.EvaluationStack => GetStackLocal(
                context,
                source.Index,
                source.IsIndirect
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference),
            RootSourceKind.ManagedAddressArgument =>
                source.Index + context.ParameterOffset,
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
