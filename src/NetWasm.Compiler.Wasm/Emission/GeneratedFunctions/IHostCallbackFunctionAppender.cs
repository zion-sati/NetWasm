// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IHostCallbackFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<string, int> indices,
        HostCallbackDeclaration callback,
        FunctionIndexMap functionIndices,
        RuntimeInitializationPlan initialization,
        InteropImportPlan interopImports,
        RuntimeImportSelection runtimeImportSelection,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries);
}
