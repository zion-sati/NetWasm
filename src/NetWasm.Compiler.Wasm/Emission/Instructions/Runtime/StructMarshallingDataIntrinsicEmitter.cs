using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Marshal's structure members read the descriptor of the type whose semantic type ID matches;
// a type without one yields 0, as native AOT's missing struct interop data does.
internal sealed class StructMarshallingDataIntrinsicEmitter(
    IStructMarshallingSource descriptors,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.NativeInt);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, typeId);
        addresses.Emit(code, 0);
        Set(code, result);
        foreach (var descriptor in descriptors.StructMarshalling)
        {
            Get(code, typeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(descriptor.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            addresses.Emit(code, descriptor.Address);
            Set(code, result);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
