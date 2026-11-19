// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// GC.InternalAllocateUninitializedArray<T>(length): a T[] whose elements need not be zeroed. An element type
// without references takes the runtime's uninitialized allocation; any other element type is allocated zeroed,
// as newarr allocates it, since the collector must not trace what the memory held. A negative length throws
// OverflowException and a failed allocation OutOfMemoryException, as newarr's do.
internal sealed class AllocateUninitializedArrayIntrinsicEmitter(
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeImportResolver imports,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var elementType = request.Method.MethodArguments[0];
        var arrayType = CliTypeIdentity.SzArray(elementType);
        var length = request.Local(0, CliValueKind.I4);
        var throwFunction = request.Instruction.Context.ManagedThrowFunction;
        Get(code, length);
        I32(code, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Overflow, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, length);
        I32(code, types.GetObjectLayout(arrayType).TypeId);
        I32(code, types.GetObjectLayout(elementType).TypeId);
        RuntimeImportSymbol allocator;
        if (elementType.StackKind == CliValueKind.ManagedReference)
        {
            allocator = RuntimeImportSymbol.AllocateReferenceArray;
        }
        else
        {
            var layout = values.GetValueLayout(elementType);
            I32(code, layout.Size);
            allocator = layout.ContainsReferences || !layout.ByReferenceOffsets.IsEmpty
                ? RuntimeImportSymbol.AllocateValueArray
                : RuntimeImportSymbol.AllocateUninitializedValueArray;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(allocator, request.Instruction.Target.RuntimeImportSelection))));
        var result = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)result)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void I32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
