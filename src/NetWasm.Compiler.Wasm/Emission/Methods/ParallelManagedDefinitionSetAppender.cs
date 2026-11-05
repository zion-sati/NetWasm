using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ParallelManagedDefinitionSetAppender(
    IManagedBodyBatchEmitterFactory batches,
    ICompiledUnitCapturer units) : IManagedDefinitionSetAppender
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
        var results = batch.Emit(orderedMethods, methods, rootMaps, target, functionIndices);
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            var method = orderedMethods[index];
            units.Capture(method.Identity.CanonicalName, result,
                methods[method.MethodKey], rootMaps[method.MethodKey]);
            functions.Add(result.Function);
            filterEnvironments[result.EnvironmentKey] = result.Environment;
            emissions.Add(result.Record);
        }
    }
}
