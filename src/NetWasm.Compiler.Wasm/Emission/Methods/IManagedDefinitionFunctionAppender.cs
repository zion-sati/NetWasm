using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IManagedDefinitionFunctionAppender
{
    void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ManagedMethodIdentity callerIdentity, EntityKey methodKey,
        StructuredMethod structured, MethodRootMap rootMap,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices);
}
