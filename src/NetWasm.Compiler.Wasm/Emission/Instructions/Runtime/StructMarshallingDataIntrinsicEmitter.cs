// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Marshal's structure members read the descriptor of the type whose semantic type ID matches;
// a type without one yields 0, as native AOT's missing struct interop data does.
internal sealed class StructMarshallingDataIntrinsicEmitter(
    IStructMarshallingSource descriptors,
    IAddressInstructionEmitter addresses,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.NativeInt);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, typeId);
        addresses.Emit(code, 0);
        Set(code, result);
        // The type's id picks its marshalling data, searched among the marshalled types.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in descriptors.StructMarshalling)
        {
            cases.Add((descriptor.TypeId, () =>
            {
                addresses.Emit(code, descriptor.Address);
                Set(code, result);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
