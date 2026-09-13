using NetWasm.Compiler.Wasm.Emission;
using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedDefinitionFunctionAppender(
    IMethodRepository methods, ISymbolFormatter symbols,
    IManagedMethodBodyEmitter bodies,
    IManagedMethodFunctionTypeResolver functionTypes) :
    IManagedDefinitionFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ManagedMethodIdentity callerIdentity, EntityKey methodKey,
        StructuredMethod structured, MethodRootMap rootMap,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(filterEnvironments);
        ArgumentNullException.ThrowIfNull(emissions);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var method = methods.GetMethod(methodKey);
        var emission = bodies.Emit(method, callerIdentity, structured, rootMap, target, functionIndices);
        functions.Add(new(symbols.Format(method), functionTypes.Resolve(method), emission.Body));
        filterEnvironments[emission.MethodKey] = emission.FilterEnvironment;
        emissions.Add(new(method.Key.ToString(), emission.MethodKey, emission));
    }
}
