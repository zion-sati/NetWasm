// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportSetAppender(
    IAsyncJSImportHelperAppender helpers) : IAsyncJSImportSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions, int importCount,
        ImmutableDictionary<string, int>.Builder indices,
        WasmEmissionRequest request, IFunctionIndexResolver functionIndices,
        InteropImportPlan interopImports, IList<ManagedBoundaryPlanEntry> boundaryEntries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(boundaryEntries);

        foreach (var binding in request.JavaScriptAsyncBindings.Values
                     .Where(binding => request.JSImportMethods.Any(method =>
                         method.Key == binding.Method))
                     .OrderBy(binding => binding.Method.Assembly.Name,
                         StringComparer.Ordinal)
                     .ThenBy(binding => binding.Method.MetadataToken))
        {
            helpers.Append(functions, importCount, indices, binding,
                functionIndices, interopImports, boundaryEntries);
        }
    }
}
