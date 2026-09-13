using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IImplicitExceptionEmitter
{
    void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind);
}
