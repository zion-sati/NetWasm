// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
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
        var references = context.ValueFrameReferences.Words;
        if (references.IsEmpty)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
        }
        else
        {
            // The runtime stores the map's first word; the rest follow the
            // frame's bytes, which it rounds up to sixteen.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(references.Length)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(unchecked((long)references[0]))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnterMapped))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
            var mapOffset = (context.ValueLayout.Size + 15) & ~15;
            for (var word = 1; word < references.Length; word++)
            {
                if (references[word] == 0) continue;
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(unchecked((long)references[word]))));
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, checked(mapOffset + word * sizeof(ulong)), sizeof(ulong));
            }
        }
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
            // A struct argument passed by its fields is stored into its frame copy, whose address its local
            // then holds, as a copied argument's local does.
            if (context.FieldArguments.TryGetValue(argumentIndex, out var fieldArgument))
            {
                addresses.Emit(code, context, offset);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                    WasmInstructionOperand.Unsigned((uint)context.ArgumentLocal(argumentIndex))));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
                var fields = fieldArgument.Shape.Fields;
                for (var field = 0; field < fields.Length; field++)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)context.ArgumentLocal(argumentIndex))));
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)(fieldArgument.FirstParameter + field))));
                    // A Vector3 field arrives as a v128 and stores 12 bytes, through the method's vector scratch.
                    ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, fields[field].Offset, fields[field].Type,
                        fields[field].Size, context.VectorScratch);
                }
                if (aggregateValues.TryInitializeArgument(code, header, context, argumentIndex))
                {
                    addresses.Emit(code, context, offset);
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)(context.ArgumentLocal(argumentIndex)))));
                }
                continue;
            }
            if (aggregateValues.TryInitializeArgument(code, header, context, argumentIndex))
            {
                // The lanes now hold the argument's value. Its frame backing, which the lanes are stored to before
                // a call takes the argument's address and reloaded from after a store of the whole argument, takes
                // the argument local's place, as an unpromoted argument's copy does: a store of the whole argument
                // writes the backing, not the caller's copy.
                addresses.Emit(code, context, offset);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)(context.ArgumentLocal(argumentIndex)))));
                continue;
            }
            addresses.Emit(code, context, offset);
            var argumentType = argumentTypes.Resolve(header, argumentIndex);
            if (argumentType.StackKind == CliValueKind.ValueType)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                    WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary)));
                var argumentLocal = context.ArgumentLocal(argumentIndex);
                var layout = values.GetValueLayout(argumentType);
                copies.Emit(code, context.ObjectTemporary, layout.Size, layout.Alignment,
                    SmallBlockEmitter.WidestChunk(layout),
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
                WasmInstructionOperand.Unsigned((uint)(context.ArgumentLocal(argumentIndex)))));
            ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, 0, argumentType,
                layouts.Target.GetStorageSize(argumentType));
        }
    }
}
