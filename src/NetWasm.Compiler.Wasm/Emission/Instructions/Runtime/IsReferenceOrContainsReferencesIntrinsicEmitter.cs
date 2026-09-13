using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class IsReferenceOrContainsReferencesIntrinsicEmitter(
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException(
                "IsReferenceOrContainsReferences requires one closed type argument");
        }
        var inspectedType = request.Method.MethodArguments[0];
        var containsReferences = !inspectedType.IsValueType ||
            inspectedType.Shape == CliTypeShape.ManagedByReference ||
            values.GetValueLayout(inspectedType).ContainsReferences;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(containsReferences ? 1 : 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.I4)))));
    }
}
