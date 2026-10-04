using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

internal interface INativeCallbackObjectWriter
{
    byte[] Write(NativeCallbackPlan callbacks, WasmTarget target);
}
