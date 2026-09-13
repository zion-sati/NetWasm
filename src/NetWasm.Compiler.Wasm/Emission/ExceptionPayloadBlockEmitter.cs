using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ExceptionPayloadBlockEmitter(ITargetLayout layouts) :
    IExceptionPayloadBlockEmitter
{
    public void Emit(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(
                layouts.Target.UsesMemory64
                    ? (byte)WasmValueType.I64
                    : (byte)WasmValueType.I32)));
    }
}
