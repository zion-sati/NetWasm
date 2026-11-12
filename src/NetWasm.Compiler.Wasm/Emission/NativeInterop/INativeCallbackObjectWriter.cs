using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

internal interface INativeCallbackObjectWriter
{
    byte[] Write(NativeCallbackPlan callbacks, ImmutableArray<NativeCallTrampoline> trampolines, WasmTarget target);
}
