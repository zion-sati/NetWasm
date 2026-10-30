using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryFillIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        SmallBlockEmitter.EmitFill(
            code,
            request.Target,
            request.Instruction.Target.InlineSmallBlockOperations,
            destination: request.Local(0, CliValueKind.NativeInt),
            value: request.Local(2, CliValueKind.I4),
            byteCount: request.Local(1, CliValueKind.NativeInt));
}
