// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationEmissionBuilder
{
    CompilationEmission Emit(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        CompilationAnalysis analysis,
        CompilationLayouts layouts,
        CompilerOptions options,
        NetWasm.Compiler.Wasm.Units.CompiledUnitState? units = null);
}
