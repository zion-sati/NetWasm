using System;
using NetWasm.Compiler.ControlFlow.Structured;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class MethodFrameEntryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentSignatureTypeResolver argumentTypes,
    IRuntimeImportResolver runtimeImports,
    IStackTraceFrameEntryEmitter stackTraceFrames,
    IValueFrameAddressEmitter valueFrameAddresses,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots) :
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
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
        }
        if (context.ValueLayout.Size != 0)
        {
            ManagedMemoryEmitter.EmitAddressConstant(
                code,
                layouts.Target,
                context.ValueLayout.Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
            foreach ((var localIndex, var offset) in context.ValueLayout.LocalOffsets)
            {
                valueFrameAddresses.Emit(code, context, offset);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)(context.LocalBase + localIndex))));
            }
            foreach ((var argumentIndex, var offset) in context.ValueLayout.ArgumentOffsets)
            {
                valueFrameAddresses.Emit(code, context, offset);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)(argumentIndex + context.ParameterOffset))));
                var argumentType = argumentTypes.Resolve(header, argumentIndex);
                ManagedMemoryEmitter.EmitStoreByType(
                    code,
                    layouts.Target,
                    0,
                    argumentType,
                    layouts.Target.GetStorageSize(argumentType));
            }
        }
        if (context.FilterEnvironment.Size != 0)
        {
            InitializeFilterEnvironment(code, context);
        }
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
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter))));
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
                EmitAddressConstant(code, values.GetValueLayout(capture.Type).Size);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Prefixed,
                    WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
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
