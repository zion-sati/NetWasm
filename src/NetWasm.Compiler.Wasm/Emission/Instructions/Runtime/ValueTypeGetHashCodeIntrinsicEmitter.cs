using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeGetHashCodeIntrinsicEmitter(
    IValueTypeHashCodeEmitter hashCode) : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var type = request.ConstrainedType ??
            throw new InvalidOperationException(
                "direct value type hashing requires its constrained type");
        hashCode.Emit(request, code, type, CliValueKind.ManagedAddress);
    }
}
