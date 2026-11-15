// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterFunctionSetAppender
{
    void Append(IList<WasmFunctionDefinition> functions, int importCount,
        IDictionary<int, int> indices, IReadOnlyList<FilterFunclet> filters,
        IReadOnlyDictionary<string, FilterEnvironmentLayout> environments,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices,
        IList<ManagedMethodEmissionRecord> managedMethodEmissions);
}
