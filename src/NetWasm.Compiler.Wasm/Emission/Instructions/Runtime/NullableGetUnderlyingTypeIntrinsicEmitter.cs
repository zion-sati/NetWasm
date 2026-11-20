// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NullableGetUnderlyingTypeIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    ITypeObjectIdReader typeIds,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var input = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeIds.Read(code, input, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        Set(code, result);

        // The type's id picks the Nullable<T> whose T it returns, searched among the program's nullables.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            var underlyingType = nullableTypes.Resolve(descriptor.Type);
            if (underlyingType is null)
            {
                continue;
            }

            cases.Add((descriptor.TypeId, () =>
            {
                WriteI32(code, typeLayouts.GetObjectLayout(underlyingType).TypeId);
                WriteI32(code, typeLayouts.TypeTypeId);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned(
                        (uint)runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject))));
                Set(code, result);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
