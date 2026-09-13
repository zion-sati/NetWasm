using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class HostCallbackSetAppender(
    IHostCallbackFunctionAppender callbacks) : IHostCallbackSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions, int importCount,
        IDictionary<string, int> callbackIndices,
        ImmutableArray<HostCallbackDeclaration> declarations,
        FunctionIndexMap functionIndices, RuntimeInitializationPlan initialization,
        InteropImportPlan interopImports,
        RuntimeImportSelection runtimeImportSelection,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(callbackIndices);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(interopImports);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        foreach (var declaration in declarations.OrderBy(
                     item => item.ExportName,
                     StringComparer.Ordinal))
        {
            callbacks.Append(functions, importCount, callbackIndices, declaration,
                functionIndices, initialization, interopImports,
                runtimeImportSelection, boundaryEntries);
        }
    }
}
