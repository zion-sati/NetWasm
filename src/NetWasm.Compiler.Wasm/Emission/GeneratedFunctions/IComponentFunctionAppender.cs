// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IComponentFunctionAppender
{
    ImmutableArray<string> Append(
        IList<WasmFunctionDefinition> functions,
        ICollection<WasmExport> exports,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
        WasmEmissionRequest request,
        WasmTarget target,
        RuntimeInitializationPlan initialization,
        int importCount,
        IReadOnlyDictionary<string, int> requestedExportIndices);
}
