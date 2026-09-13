using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStackTraceFrameEntryEmitter
{
    void Enter(
        IWasmInstructionWriter code,
        int methodId,
        RuntimeImportSelection runtimeImportSelection);
}
