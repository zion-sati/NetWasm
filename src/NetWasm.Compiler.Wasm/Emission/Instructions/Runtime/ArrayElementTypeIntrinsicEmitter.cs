using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArrayElementTypeIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    ITypeLayoutProvider types,
    IRuntimeImportResolver imports,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var local = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayElementTypeIdOffset)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(types.TypeTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)imports.Resolve(
            RuntimeImportSymbol.GetTypeObject, request.Instruction.Target.RuntimeImportSelection))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)local)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
