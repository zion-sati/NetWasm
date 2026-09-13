using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ConstructedMethodSetAppender(
    IConstructedMethodFunctionAppender constructedMethods) :
    IConstructedMethodSetAppender
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

        foreach (var callerIdentity in orderedMethods)
        {
            var methodKey = callerIdentity.CanonicalName;
            constructedMethods.Append(functions, filterEnvironments, emissions, callerIdentity,
                methodInstances[methodKey], methods[methodKey], rootMaps[methodKey],
                target, functionIndices);
        }
    }
}
