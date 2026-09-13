using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterFunctionAppender(
    IFilterSequenceEmitterFactory sequenceEmitters,
    IFilterFuncletEmitter filters,
    IFilterEmissionCountMerger emissionCounts) : IFilterFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        IDictionary<int, int> indices,
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        IList<ManagedMethodEmissionRecord> managedMethodEmissions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(managedMethodEmissions);

        indices.Add(filter.Id, importCount + functions.Count);
        var sequence = sequenceEmitters.Create(target, functionIndices);
        var emission = filters.Emit(filter, environment, sequence);
        functions.Add(new WasmFunctionDefinition(
            $"filter.{filter.Id}",
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress),
            emission.Body));
        emissionCounts.Merge(
            managedMethodEmissions,
            filter.Method,
            emission.OriginalBlockEmissionCounts);
    }
}
