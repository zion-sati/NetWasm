using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterFunctionSetAppender(
    IFilterFunctionAppender filters) : IFilterFunctionSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions, int importCount,
        IDictionary<int, int> indices, IReadOnlyList<FilterFunclet> funclets,
        IReadOnlyDictionary<string, FilterEnvironmentLayout> environments,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices,
        IList<ManagedMethodEmissionRecord> managedMethodEmissions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(funclets);
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(managedMethodEmissions);

        foreach (var filter in funclets.OrderBy(item => item.Id))
        {
            filters.Append(functions, importCount, indices, filter,
                environments[ManagedMethodBodyKey.Resolve(filter.Method)],
                target, functionIndices, managedMethodEmissions);
        }
    }
}
