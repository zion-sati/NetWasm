using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// A call without a constrained prefix that names the type dispatches on its receiver instead,
// each receiver's case hashing its own type.
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
