// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Array.InternalPrimitiveElementType: the array's element type as upstream's
// GetCorElementTypeOfElementType reads it from the method table, as a CorElementType: a
// primitive's own, an enum's underlying one, and ELEMENT_TYPE_CLASS for anything else. Every
// array is of a type the program constructs, so the array's element type ID is compared with the
// element types of the constructed arrays of primitives and enums, whatever their rank; a program
// constructing none answers ELEMENT_TYPE_CLASS without a comparison.
internal sealed class ArrayPrimitiveElementTypeIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    ITypeLayoutProvider types,
    IRuntimeObjectLayout objects,
    IArrayReceiverValidator receivers,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    private const int ElementTypeClass = 0x12;

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var array = request.Local(0, CliValueKind.ManagedReference);
        var element = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.I4);
        receivers.Validate(code, array, request.Instruction.Context.ManagedThrowFunction);
        Get(code, array);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayElementTypeIdOffset)));
        Set(code, element);
        I32(code, ElementTypeClass);
        Set(code, result);
        var compared = new HashSet<int>();
        // The element type's id picks its primitive kind, searched among the program's arrays' elements.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            if (descriptor.Type.Shape is not (CliTypeShape.SzArray or CliTypeShape.Array) ||
                descriptor.Type.ContainsGenericParameters ||
                descriptor.Type.ElementType is not { } elementType ||
                Kind(elementType) is not { } kind ||
                !types.GetObjectLayout(elementType, out var elementLayout))
            {
                continue;
            }
            var elementId = elementLayout.TypeId;
            if (!compared.Add(elementId))
            {
                continue;
            }
            cases.Add((elementId, () =>
            {
                I32(code, kind);
                Set(code, result);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(element, false, cases, () => { }));
    }

    // CorElementType of a primitive, or of an enum's underlying primitive.
    private static int? Kind(CliTypeIdentity type) => (type.StackStorageType ?? type).CanonicalName switch
    {
        "primitive:bool" => 0x02,
        "primitive:char" => 0x03,
        "primitive:i1" => 0x04,
        "primitive:u1" => 0x05,
        "primitive:i2" => 0x06,
        "primitive:u2" => 0x07,
        "primitive:i4" => 0x08,
        "primitive:u4" => 0x09,
        "primitive:i8" => 0x0A,
        "primitive:u8" => 0x0B,
        "primitive:f4" => 0x0C,
        "primitive:f8" => 0x0D,
        "primitive:nativeint" => 0x18,
        "primitive:nativeuint" => 0x19,
        _ => null,
    };

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void I32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
