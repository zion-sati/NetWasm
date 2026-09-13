using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class HostCallbackStringArgumentMarshaller(
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IHostCallbackStringArgumentMarshaller
{
    public void Emit(
        IWasmInstructionWriter code,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target)
    {
        Require(target.Imports.StringLength, "string host result helpers were not emitted");
        Require(target.Imports.CopyStringUtf16, "string host result helpers were not emitted");
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.StringLength.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed((int)((uint.MaxValue - 2 * sizeof(int)) / sizeof(char)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.StringTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.AllocateString)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        EmitAllocationFailure(code, objectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.CopyStringUtf16.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailure(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void Require(OptionalFunctionIndex index, string message)
    {
        if (!index.IsPresent)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed class HostCallbackByteArrayArgumentMarshaller(
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IHostCallbackByteArrayArgumentMarshaller
{
    public void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target)
    {
        Require(target.Imports.ByteLength, "byte host result helpers were not emitted");
        Require(target.Imports.CopyBytes, "byte host result helpers were not emitted");
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.ByteLength.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        var elementType = arrayType.ElementType!;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(arrayType).TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(elementType).TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(values.GetValueLayout(elementType).Size)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.AllocateValueArray)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        EmitAllocationFailure(code, objectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(handleParameter))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.CopyBytes.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailure(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void Require(OptionalFunctionIndex index, string message)
    {
        if (!index.IsPresent)
        {
            throw new InvalidOperationException(message);
        }
    }
}
