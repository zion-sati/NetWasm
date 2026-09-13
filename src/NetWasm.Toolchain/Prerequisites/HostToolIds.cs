using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public static class HostToolIds
{
    public const string Node = "node";
    public const string WasmTools = "wasm-tools";
    public const string Wasmtime = "wasmtime";
    public const string WasmLd = "wasm-ld";

    public static ImmutableArray<string> Required { get; } =
        [Node, WasmLd];
}
