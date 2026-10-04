using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStackTraceFrameLocationEmitter
{
    void Update(
        IWasmInstructionWriter code,
        int methodId,
        int symbolId,
        RuntimeImportSelection runtimeImportSelection);
}
