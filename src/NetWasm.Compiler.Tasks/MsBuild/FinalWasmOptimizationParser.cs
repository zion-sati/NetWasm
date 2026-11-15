// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal static class FinalWasmOptimizationParser
{
    public static FinalWasmOptimization Parse(string value) => value switch
    {
        "None" => FinalWasmOptimization.None,
        "O0" => FinalWasmOptimization.O0,
        "O1" => FinalWasmOptimization.O1,
        "O2" => FinalWasmOptimization.O2,
        "O3" => FinalWasmOptimization.O3,
        "Os" => FinalWasmOptimization.Os,
        "Oz" => FinalWasmOptimization.Oz,
        _ => throw new InvalidOperationException(
            "NetWasmOptimization must be None, O0, O1, O2, O3, Os or Oz."),
    };
}
