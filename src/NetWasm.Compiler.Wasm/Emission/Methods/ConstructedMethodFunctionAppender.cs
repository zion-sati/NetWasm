using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ConstructedMethodFunctionAppender(
    IManagedMethodBodyEmitter bodies,
    IManagedMethodFunctionTypeResolver functionTypes) :
    IConstructedMethodFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ManagedMethodIdentity callerIdentity, MethodInstanceModel method,
        StructuredMethod structured, MethodRootMap rootMap,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(filterEnvironments);
        ArgumentNullException.ThrowIfNull(emissions);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var emission = bodies.Emit(method.Definition, callerIdentity, structured, rootMap, target,
            functionIndices, method);
        functions.Add(new(method.CanonicalName, functionTypes.Resolve(method), emission.Body));
        filterEnvironments[emission.MethodKey] = emission.FilterEnvironment;
        emissions.Add(new(method.CanonicalName, emission.MethodKey, emission));
    }
}
