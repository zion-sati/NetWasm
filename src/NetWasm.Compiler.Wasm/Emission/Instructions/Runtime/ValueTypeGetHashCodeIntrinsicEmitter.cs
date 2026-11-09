using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeGetHashCodeIntrinsicEmitter(
    IValueTypeHashCodeEmitter hashCode,
    IValueTypeHashReceiverResolver receivers) : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var receiver = receivers.Resolve(request);
        hashCode.Emit(request, code, receiver.Type, receiver.Kind);
    }
}
