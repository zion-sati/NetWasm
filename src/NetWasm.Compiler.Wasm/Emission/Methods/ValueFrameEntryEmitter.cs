using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ValueFrameEntryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentSignatureTypeResolver argumentTypes,
    IRuntimeImportResolver runtimeImports,
    IValueFrameAddressEmitter addresses,
    IValueFrameInitializationEmitter initialization,
    IValueCopyEmitter copies,
    IManagedAggregateValueInitializer aggregateValues) : IValueFrameEntryEmitter
{
    public void Emit(IWasmInstructionWriter code, StructuredMethodHeader header, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ValueLayout.Size == 0)
        {
            return;
        }

        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, context.ValueLayout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
        initialization.Initialize(code, header, context);
        foreach ((var localIndex, var offset) in context.ValueLayout.LocalOffsets)
        {
            addresses.Emit(code, context, offset);
            var targetLocal = context.AddressTakenScalarPromotions.ShadowLocals
                .TryGetValue(localIndex, out var shadowLocal)
                ? shadowLocal
                : context.LocalBase + localIndex;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)targetLocal)));
        }
        foreach ((var argumentIndex, var offset) in context.ValueLayout.ArgumentOffsets)
        {
            if (aggregateValues.TryInitializeArgument(code, header, context, argumentIndex))
            {
                continue;
            }
            addresses.Emit(code, context, offset);
            var argumentType = argumentTypes.Resolve(header, argumentIndex);
            if (argumentType.StackKind == CliValueKind.ValueType)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                    WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary)));
                var argumentLocal = argumentIndex + context.ParameterOffset;
                var layout = values.GetValueLayout(argumentType);
                copies.Emit(code, context.ObjectTemporary, layout.Size, layout.Alignment,
                    writer => writer.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)argumentLocal))),
                    context);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)argumentLocal)));
                continue;
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)(argumentIndex + context.ParameterOffset))));
            ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, 0, argumentType,
                layouts.Target.GetStorageSize(argumentType));
        }
    }
}
