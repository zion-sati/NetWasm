using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStackTraceFrameExitEmitter
{
    void Leave(
        IWasmInstructionWriter code,
        int methodId,
        RuntimeImportSelection runtimeImportSelection);
}
