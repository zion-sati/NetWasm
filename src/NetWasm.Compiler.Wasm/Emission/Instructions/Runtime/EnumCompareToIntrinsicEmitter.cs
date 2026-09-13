using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumCompareToIntrinsicEmitter(
    IEnumCompareToEmitter emitter) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        emitter.EmitCompareTo(
            code,
            request.Instruction.Stack[request.ArgumentBase],
            request.ConstrainedType,
            request.Local(0, request.Instruction.Stack[request.ArgumentBase]),
            request.Local(1, CliValueKind.ManagedReference),
            request.Local(0, CliValueKind.I4),
            context.ObjectTemporary,
            context.NumericTemporaryI4);
    }
}
