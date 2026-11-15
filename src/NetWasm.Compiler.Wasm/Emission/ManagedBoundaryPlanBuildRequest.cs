// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ManagedBoundaryPlanBuildRequest(
    IReadOnlyList<WasmFunctionDefinition> Functions,
    int ImportedFunctionCount,
    int FunctionIndex,
    string ExportName,
    ManagedBoundaryKind Kind,
    bool IsOutwardFacing);
