// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel;

using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Optimization;

public sealed record ComponentCoreModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint = null,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    bool StructuredDiagnostics = false)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }
}
