// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentBuildRequest(
    string CoreModulePath,
    string RuntimeModulePath,
    string WitPath,
    string? World,
    string OutputPath,
    ComponentTarget Target,
    ComponentManifestInputs ManifestInputs,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    bool StructuredDiagnostics = false)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }

    // Write the linked plain core module, without WASI, rather than a component.
    public bool PlainCoreModule { get; init; }
}
