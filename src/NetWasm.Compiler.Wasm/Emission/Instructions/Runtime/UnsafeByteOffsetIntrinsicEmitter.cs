using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class UnsafeByteOffsetIntrinsicEmitter(
    ITargetLayout layouts) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(
                1,
                CliValueKind.ManagedAddress))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(
                0,
                CliValueKind.ManagedAddress))));
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(
                0,
                CliValueKind.NativeInt))));
    }
}
