using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ParallelManagedDefinitionSetAppender(
    IManagedBodyBatchEmitterFactory batches) : IManagedDefinitionSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ImmutableArray<ManagedDefinitionEmission> orderedMethods,
        IReadOnlyDictionary<EntityKey, StructuredMethod> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(filterEnvironments);
        ArgumentNullException.ThrowIfNull(emissions);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(rootMaps);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);
        if (orderedMethods.IsEmpty) return;

        using var batch = batches.Create();
        foreach (var result in batch.Emit(orderedMethods, methods, rootMaps,
                     target, functionIndices))
        {
            functions.Add(result.Function);
            filterEnvironments[result.EnvironmentKey] = result.Environment;
            emissions.Add(result.Record);
        }
    }
}
