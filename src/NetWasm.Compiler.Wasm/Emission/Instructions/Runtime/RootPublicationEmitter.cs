using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class RootPublicationEmitter(
    ITargetLayout layouts,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots) : IRootPublicationEmitter
{
    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, bool constructorCall = false)
    {
        var context = request.Context;
        if (!context.RootMap.Safepoints.TryGetValue(
                request.Instruction.Offset,
                out var safepoint) ||
            context.RootMap.SlotCount == 0)
        {
            return;
        }
        filterEnvironmentRoots.Emit(code, context);
        var roots = constructorCall
            ? safepoint.ConstructorCallRoots
            : safepoint.Roots;
        if (constructorCall && roots.IsEmpty)
        {
            return;
        }
        ClearSlots(code, context);
        foreach (var source in roots)
        {
            var sourceLocal = GetSourceLocal(context, source);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.RootFrame))));
            var rootSlot = context.RootMap.Slots[source];
            if (rootSlot != 0)
            {
                EmitAddressConstant(code, rootSlot * layouts.Target.ObjectReferenceSize);
                EmitAddressAdd(code);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(sourceLocal))));
            if (source.IsIndirect || source.Kind == RootSourceKind.Local &&
                context.ValueLayout.SpilledScalarLocals.Contains(source.Index))
            {
                ManagedMemoryEmitter.EmitLoadBySize(
                    code,
                    layouts.Target,
                    source.IsIndirect ? source.ByteOffset : 0,
                    layouts.Target.ObjectReferenceSize);
            }
            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                0,
                layouts.Target.ObjectReferenceSize);
        }
    }

    private void ClearSlots(IWasmInstructionWriter code, MethodEmissionContext context)
    {
        for (var slot = 0; slot < context.RootMap.SlotCount; slot++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.RootFrame))));
            if (slot != 0)
            {
                EmitAddressConstant(code, slot * layouts.Target.ObjectReferenceSize);
                EmitAddressAdd(code);
            }
            EmitAddressConstant(code, 0);
            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                0,
                layouts.Target.ObjectReferenceSize);
        }
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

    private void EmitAddressAdd(IWasmInstructionWriter code)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
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
