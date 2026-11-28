// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Raw;

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel.Debugging;

public sealed record RawModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz)
{
    public WasmOptimizationOverrides? OptimizationOverrides { get; init; }
    public ImmutableArray<WasmInternalExport> InternalRuntimeExports { get; init; } = [];
    public ImmutableArray<WasmInternalExport> InternalApplicationExports { get; init; } = [];

    // Debug information (NetWasmDebugInfo): the module's names, and DWARF lines with the program's method symbols.
    public WasmDebugInformation? DebugInformation { get; init; }
}
