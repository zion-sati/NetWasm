using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportArgumentEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int local,
        MethodEmissionContext context,
        InteropImportPlan interop);
}
