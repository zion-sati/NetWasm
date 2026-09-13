using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RequestedExportSetAppender(
    IRequestedExportFunctionAppender exports) : IRequestedExportSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions, int importCount,
        IDictionary<string, int> requestedExportIndices,
        IDictionary<string, int> asyncHelperIndices,
        IReadOnlyDictionary<string, EntityKey> requestedExports,
        IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> asyncBindings,
        RuntimeInitializationPlan initialization, bool hasFinalizers,
        WasmModuleProfile profile,
        IFunctionIndexResolver functionIndices,
        ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(requestedExportIndices);
        ArgumentNullException.ThrowIfNull(asyncHelperIndices);
        ArgumentNullException.ThrowIfNull(requestedExports);
        ArgumentNullException.ThrowIfNull(asyncBindings);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        foreach ((var name, var methodKey) in requestedExports.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            exports.Append(functions, importCount, requestedExportIndices,
                asyncHelperIndices, name, methodKey, asyncBindings, initialization,
                hasFinalizers, profile, functionIndices, boundaryEntries);
        }
    }
}
