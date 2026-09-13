using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetHashCodeIntrinsicEmitter(
    IEnumHashCodeEmitter emitter) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        var receiverKind = request.Instruction.Stack[request.ArgumentBase];
        emitter.EmitHashCode(
            code,
            receiverKind,
            request.ConstrainedType ?? request.Method.DeclaringType,
            request.Local(0, receiverKind),
            request.Local(0, CliValueKind.I4),
            context.NumericTemporaryI4,
            context.NumericTemporaryI8);
    }
}
