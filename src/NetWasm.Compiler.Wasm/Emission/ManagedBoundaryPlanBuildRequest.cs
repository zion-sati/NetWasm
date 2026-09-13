using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ManagedBoundaryPlanBuildRequest(
    IReadOnlyList<WasmFunctionDefinition> Functions,
    int ImportedFunctionCount,
    int FunctionIndex,
    string ExportName,
    ManagedBoundaryKind Kind,
    bool IsOutwardFacing);
