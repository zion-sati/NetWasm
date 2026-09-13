using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumValueReturnEmitter(
    IValueFrameAddressEmitter addresses,
    IValueLayoutProvider values,
    ITargetLayout layouts) : IEnumValueReturnEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        RuntimeIntrinsicEmissionRequest request,
        int valueLocal)
    {
        var instruction = request.Instruction;
        var returnType = request.Method.Signature.ReturnSignatureType;
        var returnOffset = instruction.Context.ValueLayout.TemporaryOffsets[
            instruction.Instruction.Offset];
        var result = request.Local(0, CliValueKind.ValueType);

        addresses.Emit(code, instruction.Context, returnOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueLocal)));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            0,
            returnType,
            values.GetValueLayout(returnType).Size);
    }
}
