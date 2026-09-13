using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumHasFlagIntrinsicEmitter(
    IEnumHasFlagEmitter emitter) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        var receiverKind = request.Instruction.Stack[request.ArgumentBase];
        emitter.EmitHasFlag(
            code,
            request.Local(0, receiverKind),
            request.Local(1, CliValueKind.ManagedReference),
            request.Local(0, CliValueKind.I4),
            context.NumericTemporaryI4,
            receiverKind,
            request.ConstrainedType);
    }
}
