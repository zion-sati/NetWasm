// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ParallelConstructedMethodSetAppender(
    IManagedBodyBatchEmitterFactory batches,
    ICompiledUnitCapturer units) : IConstructedMethodSetAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ImmutableArray<ManagedMethodIdentity> orderedMethods,
        IReadOnlyDictionary<string, MethodInstanceModel> methodInstances,
        IReadOnlyDictionary<string, StructuredMethod> methods,
        IReadOnlyDictionary<string, MethodRootMap> rootMaps,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(filterEnvironments);
        ArgumentNullException.ThrowIfNull(emissions);
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(rootMaps);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);
        if (orderedMethods.IsEmpty) return;

        using var batch = batches.Create();
        batch.Emit(orderedMethods, methodInstances, methods, rootMaps, target, functionIndices, (index, result) =>
        {
            var key = orderedMethods[index].CanonicalName;
            units.Capture(key, result, methods[key], rootMaps[key]);
            functions.Add(result.Function);
            filterEnvironments[result.EnvironmentKey] = result.Environment;
            emissions.Add(result.Record);
        });
    }
}
