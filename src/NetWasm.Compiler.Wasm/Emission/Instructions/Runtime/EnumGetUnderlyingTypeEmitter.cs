// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetUnderlyingTypeEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments,
    IKeyedDispatchEmitter keyedDispatch) : IEnumGetUnderlyingTypeEmitter
{
    public void EmitGetUnderlyingType(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var input = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, input, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)result)));
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = types.GetTypeDefinition(entry.Type).EnumUnderlyingType;
            cases.Add((entry.TypeId, () =>
            {
                WriteI32(code, typeLayouts.GetObjectLayout(underlying).TypeId);
                WriteI32(code, typeLayouts.TypeTypeId);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject))));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)result)));
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
