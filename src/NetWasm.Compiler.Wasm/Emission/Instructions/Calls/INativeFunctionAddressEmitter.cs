using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface INativeFunctionAddressEmitter
{
    void Emit(
        NativeCallbackMethodPlan callback,
        IWasmInstructionWriter code);
}
