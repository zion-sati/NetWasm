// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel.Debugging;

public sealed record ComponentPackageRequest(
    string CoreModulePath,
    string WitPath,
    string? World,
    string OutputPath,
    ComponentTarget Target,
    string? RuntimeModulePath = null,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    bool StructuredDiagnostics = false)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }

    // Debug information (NetWasmDebugInfo): the module's names, and DWARF lines with the program's method symbols.
    public WasmDebugInformation? DebugInformation { get; init; }

    // Write the linked core module itself (the program's world imports only, no WASI) rather
    // than a component.
    public bool PlainCoreModule { get; init; }
}
