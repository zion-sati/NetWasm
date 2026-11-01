using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public static class HostToolIds
{
    public const string Node = "node";
    public const string WasmLd = "wasm-ld";
    public const string BinaryenWasmMerge = "binaryen.wasm-merge";
    public const string BinaryenWasmOpt = "binaryen.wasm-opt";

    public static ImmutableArray<string> Known { get; } =
        [Node, WasmLd, BinaryenWasmMerge, BinaryenWasmOpt];
}
