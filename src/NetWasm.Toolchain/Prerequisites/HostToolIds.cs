// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public static class HostToolIds
{
    public const string Node = "node";
    public const string WasmLd = "wasm-ld";
    public const string BinaryenWasmOpt = "binaryen.wasm-opt";
    public const string WasmTools = "wasm-tools";

    public static ImmutableArray<string> Known { get; } =
        [Node, WasmLd, BinaryenWasmOpt, WasmTools];
}
