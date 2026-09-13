using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class WasmTargetResolver : IWasmTargetResolver
{
    public WasmTarget Resolve(string target) => target switch
    {
        "wasm32" => WasmTarget.Wasm32,
        "wasm64" => WasmTarget.Wasm64,
        _ => throw new ArgumentException(
            "NetWasmTarget must be 'wasm32' or 'wasm64'.",
            nameof(target)),
    };
}
