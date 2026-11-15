// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationRootMapBuilder
{
    ReachableProgram Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationLayouts layouts,
        ReachableProgram program,
        NetWasm.Compiler.Wasm.Units.CompiledUnitState? units = null);
}
