using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IMethodExceptionBoundaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        Action<MethodEmissionContext> emitBody);
}
