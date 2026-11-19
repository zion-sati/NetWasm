// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

/// <summary>
/// Builds the number array a JavaScript value becomes in managed code from the elements the host staged for it: a
/// block of linear memory from the runtime's allocator, given as its address and the array's length (-1 for null).
/// The block is freed once copied, and before an allocation failure throws.
/// </summary>
internal interface IStagedValueArrayEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int addressLocal,
        int lengthLocal,
        int destination,
        int objectTemporary,
        OptionalFunctionIndex throwFunction);
}

internal sealed class StagedValueArrayEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IStagedValueArrayEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int addressLocal,
        int lengthLocal,
        int destination,
        int objectTemporary,
        OptionalFunctionIndex throwFunction)
    {
        var elementType = arrayType.ElementType!;
        var elementSize = values.GetValueLayout(elementType).Size;
        LocalGet(code, lengthLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code, 0);
        LocalSet(code, destination);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        LocalGet(code, lengthLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(arrayType).TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(elementType).TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(elementSize)));
        Call(code, RuntimeImportSymbol.AllocateValueArray);
        LocalSet(code, objectTemporary);
        LocalGet(code, objectTemporary);
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Free(code, addressLocal);
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        // memory.copy(the array's elements, the staged block, length * element size); the runtime keeps a pointer to
        // an array's elements under every collector.
        LocalGet(code, objectTemporary);
        ManagedMemoryEmitter.EmitArrayData(code, layouts.Target, inlineData: false);
        LocalGet(code, addressLocal);
        LocalGet(code, lengthLocal);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, elementSize);
        ManagedMemoryEmitter.EmitAddressMultiply(code, layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        Free(code, addressLocal);
        LocalGet(code, objectTemporary);
        LocalSet(code, destination);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void Free(IWasmInstructionWriter code, int addressLocal)
    {
        LocalGet(code, addressLocal);
        Call(code, RuntimeImportSymbol.NativeFree);
    }

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(symbol))));

    private static void LocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void LocalSet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
