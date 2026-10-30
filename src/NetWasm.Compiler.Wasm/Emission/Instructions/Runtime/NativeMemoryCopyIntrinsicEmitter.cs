using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryCopyIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        SmallBlockEmitter.EmitCopy(
            code,
            request.Target,
            request.Instruction.Target.InlineSmallBlockOperations,
            destination: request.Local(1, CliValueKind.NativeInt),
            source: request.Local(0, CliValueKind.NativeInt),
            byteCount: request.Local(2, CliValueKind.NativeInt));
}
