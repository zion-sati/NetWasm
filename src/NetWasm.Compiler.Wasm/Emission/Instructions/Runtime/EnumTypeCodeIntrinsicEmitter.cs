using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumTypeCodeIntrinsicEmitter(
    IEnumTypeCodeEmitter emitter,
    IEnumValueReturnEmitter valueReturns) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        var receiverKind = request.Instruction.Stack[request.ArgumentBase];
        var valueTypeResult = request.Method.Signature.ReturnType == CliValueKind.ValueType;
        var result = valueTypeResult
            ? context.NumericTemporaryI4
            : request.Local(0, CliValueKind.I4);
        emitter.EmitTypeCode(
            code,
            request.Local(0, receiverKind),
            result,
            context.NumericTemporaryI4,
            request.ConstrainedType,
            receiverKind);
        if (valueTypeResult)
            valueReturns.Emit(code, request, result);
    }
}
