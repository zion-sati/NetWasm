// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArrayCreateInstanceIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeImportResolver imports,
    IRootPublicationEmitter roots,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var elementId = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.ManagedReference);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, elementId);
        roots.Emit(request.Instruction, code);
        addresses.Emit(code, 0);
        Set(code, result);
        // The element's type id picks the vector type to allocate, searched among the program's vectors' elements.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            if (descriptor.Type.Shape != CliTypeShape.SzArray || descriptor.Type.ContainsGenericParameters)
                continue;
            var element = descriptor.Type.ElementType!;
            // An array of references is the shared array (ArrayCreateSharedInstanceIntrinsicEmitter), whose casts
            // check its element: a vector type the program only names is not one the analysis saw allocated.
            if (element.StackKind == CliValueKind.ManagedReference)
                continue;
            // An element type the program never lays out has no type id, so no runtime Type names it
            // and Array.CreateInstance cannot ask for its vector (a DynamicMetaObject[] parameter, say).
            if (!types.GetObjectLayout(element, out var elementLayout))
                continue;
            var typeId = elementLayout.TypeId;
            cases.Add((typeId, () =>
            {
                Get(code, request.Local(1, CliValueKind.I4));
                I32(code, descriptor.TypeId);
                I32(code, typeId);
                var import = RuntimeImportSymbol.AllocateReferenceArray;
                if (element.StackKind != CliValueKind.ManagedReference)
                {
                    I32(code, values.GetValueLayout(element).Size);
                    import = RuntimeImportSymbol.AllocateValueArray;
                }
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)imports.Resolve(import,
                        request.Instruction.Target.RuntimeImportSelection))));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                    WasmInstructionOperand.Unsigned((uint)result)));
                addresses.Emit(code, AddressOperation.EqualZero);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(elementId, false, cases, () => { }));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
