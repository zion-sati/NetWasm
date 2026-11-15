// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission;

public interface IManagedBoundaryPlanValidator
{
    void Validate(
        IReadOnlyList<WasmFunctionDefinition> functions,
        IReadOnlyList<WasmExport> exports,
        int importedFunctionCount,
        IEnumerable<ManagedBoundaryPlanEntry> entries);
}
