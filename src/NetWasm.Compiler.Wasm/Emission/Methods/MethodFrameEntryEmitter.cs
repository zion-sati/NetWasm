using System;
using NetWasm.Compiler.ControlFlow.Structured;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class MethodFrameEntryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IStackTraceFrameEntryEmitter stackTraceFrames,
    IValueFrameAddressEmitter valueFrameAddresses,
    IValueFrameEntryEmitter valueFrames,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots,
    IManagedByRefAggregateInitializationEmitter managedByRefAggregates) :
    IMethodFrameEntryEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);

        if (context.StackTraceMethodId != 0)
        {
            stackTraceFrames.Enter(
                code,
                context.StackTraceMethodId,
                context.RuntimeImportSelection);
        }

        if (context.RootSlotCount != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(context.RootSlotCount)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                context.ExceptionRootSlots.Count == 0
                    ? WasmOpcodes.LocalSet
                    : WasmOpcodes.LocalTee,
                WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
            InitializeExceptionRoots(code, context);
            foreach (var (source, slot) in context.AuthoritativeRootSlots.OrderBy(pair => pair.Value))
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
                if (source.Kind == RootSourceKind.Argument)
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)(source.Index + context.ParameterOffset))));
                else
                    ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, 0);
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target,
                    checked(slot * layouts.Target.ObjectReferenceSize), layouts.Target.ObjectReferenceSize);
            }
        }
        valueFrames.Emit(code, header, context);
        if (context.FilterEnvironment.Size != 0)
        {
            InitializeFilterEnvironment(code, context);
        }
        managedByRefAggregates.Emit(code, context);
    }

    private void InitializeExceptionRoots(
        IWasmInstructionWriter code,
        MethodEmissionContext context)
    {
        if (context.ExceptionRootSlots.Count == 0)
        {
            return;
        }

        var byteOffset = checked(
            context.RootMap.SlotCount * layouts.Target.ObjectReferenceSize);
        if (context.ExceptionRootSlots.Count == 1)
        {
            ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, 0);
            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                byteOffset,
                layouts.Target.ObjectReferenceSize);
            return;
        }

        if (byteOffset != 0)
        {
            ManagedMemoryEmitter.EmitAddressConstant(
                code,
                layouts.Target,
                byteOffset);
            ManagedMemoryEmitter.EmitAddressAdd(code, layouts.Target);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        ManagedMemoryEmitter.EmitAddressConstant(
            code,
            layouts.Target,
            checked(context.ExceptionRootSlots.Count *
                layouts.Target.ObjectReferenceSize));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    private void InitializeFilterEnvironment(
        IWasmInstructionWriter code,
        MethodEmissionContext context)
    {
        if (context.FilterEnvironment.RootSlotCount != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(context.FilterEnvironment.RootSlotCount)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
        }
        valueFrameAddresses.Emit(
            code,
            context,
            context.FilterEnvironment.RootFrameOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.AddressSize);
        valueFrameAddresses.Emit(code, context, context.FilterEnvironment.ParentRootFrameOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.AddressSize);
        foreach (var capture in context.FilterEnvironment.Captures.Values
                     .Where(capture => capture.Slot.IsArgument)
                     .OrderBy(capture => capture.Slot.Index))
        {
            valueFrameAddresses.Emit(code, context, capture.Offset);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)(capture.Slot.Index + context.ParameterOffset))));
            if (capture.Type.StackKind == CliValueKind.ValueType)
            {
                SmallBlockEmitter.EmitKnownSizeCopy(code, context, values.GetValueLayout(capture.Type).Size,
                    writer => EmitAddressConstant(writer, values.GetValueLayout(capture.Type).Size));
            }
            else
            {
                ManagedMemoryEmitter.EmitStoreByType(
                    code,
                    layouts.Target,
                    0,
                    capture.Type,
                    layouts.Target.GetStorageSize(capture.Type));
            }
        }
        filterEnvironmentRoots.Emit(code, context);
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
        }
    }
}
