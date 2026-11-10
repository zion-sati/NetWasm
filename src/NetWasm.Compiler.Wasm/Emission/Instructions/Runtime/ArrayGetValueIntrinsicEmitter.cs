using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArrayGetValueIntrinsicEmitter(
    IArrayReceiverValidator receivers,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions,
    ITypeDescriptorSource descriptors,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var array = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        receivers.Validate(code, array, request.Instruction.Context.ManagedThrowFunction);
        var elementId = request.Instruction.Context.NumericTemporaryI4;
        var underlying = request.Instruction.Context.NumericTemporaryI4Second;
        var payloadOffset = request.Instruction.Context.ObjectTemporary;
        Get(code, array);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayElementTypeIdOffset)));
        Set(code, elementId);
        I32(code, 0); Set(code, underlying);
        addresses.Emit(code, 0); Set(code, payloadOffset);
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            // T is boxed only for Nullable<T> array elements; a Nullable<T> that no array holds
            // has no boxed layout and cannot be an element here.
            var type = nullableTypes.Resolve(descriptor.Type);
            if (type is null || !types.GetObjectLayout(type, out var boxed)) continue;
            Get(code, elementId); I32(code, descriptor.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            I32(code, boxed.TypeId); Set(code, underlying);
            addresses.Emit(code, WasmTargetLayout.Align(sizeof(byte), values.GetValueLayout(type).Alignment));
            Set(code, payloadOffset);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        Get(code, array);
        Get(code, request.Local(1, CliValueKind.I4));
        Get(code, underlying);
        Get(code, payloadOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.ArrayGetValue, request.Instruction.Target.RuntimeImportSelection))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)result)));
        addresses.Emit(code, 1);
        addresses.Emit(code, AddressOperation.Equal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
