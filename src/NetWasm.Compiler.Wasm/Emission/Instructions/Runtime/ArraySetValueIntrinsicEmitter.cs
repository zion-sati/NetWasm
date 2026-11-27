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

internal sealed class ArraySetValueIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver imports,
    IAddressInstructionEmitter addresses,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var element = request.Instruction.Context.NumericTemporaryI4;
        var underlying = request.Instruction.Context.NumericTemporaryI4Second;
        var offset = request.Instruction.Context.ObjectTemporary;
        Get(code, request.Local(0, CliValueKind.ManagedReference));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayElementTypeIdOffset)));
        Set(code, element);
        I32(code, 0); Set(code, underlying);
        addresses.Emit(code, 0); Set(code, offset);
        // The element type's id picks the Nullable<T> whose boxes the array's elements unbox, searched among them.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            // A Nullable<T> that no array holds has no boxed layout and cannot be an element here.
            var type = nullableTypes.Resolve(descriptor.Type);
            if (type is null || !types.GetObjectLayout(type, out var boxed)) continue;
            cases.Add((descriptor.TypeId, () =>
            {
                I32(code, boxed.TypeId); Set(code, underlying);
                addresses.Emit(code, WasmTargetLayout.Align(sizeof(byte), values.GetValueLayout(type).Alignment));
                Set(code, offset);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(element, false, cases, () => { }));
        Get(code, request.Local(0, CliValueKind.ManagedReference));
        Get(code, request.Local(1, CliValueKind.I4));
        Get(code, request.Local(2, CliValueKind.ManagedReference));
        Get(code, underlying); Get(code, offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(
                request.Instruction.Target.RuntimeImportSelection.ArraySetValue,
                request.Instruction.Target.RuntimeImportSelection))));
        Set(code, request.Local(0, CliValueKind.I4));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
