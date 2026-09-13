using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeEqualsIntrinsicEmitter(
    IValueTypeEqualsEmitter equals) : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var type = request.ConstrainedType ??
            throw new InvalidOperationException(
                "direct value type equality requires its constrained type");
        equals.Emit(request, code, type, CliValueKind.ManagedAddress);
    }
}
