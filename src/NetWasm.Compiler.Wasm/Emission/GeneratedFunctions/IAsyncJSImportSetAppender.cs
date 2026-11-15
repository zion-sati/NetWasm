// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSImportSetAppender
{
    void Append(IList<WasmFunctionDefinition> functions, int importCount,
        ImmutableDictionary<string, int>.Builder indices,
        WasmEmissionRequest request, IFunctionIndexResolver functionIndices,
        InteropImportPlan interopImports, IList<ManagedBoundaryPlanEntry> boundaryEntries);
}
