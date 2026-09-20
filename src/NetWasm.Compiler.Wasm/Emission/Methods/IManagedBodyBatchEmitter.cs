using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ManagedBodyEmission(
    WasmFunctionDefinition Function,
    string EnvironmentKey,
    FilterEnvironmentLayout Environment,
    ManagedMethodEmissionRecord Record);

internal interface IManagedBodyBatchEmitter : IDisposable
{
    ManagedBodyEmission[] Emit(
        ImmutableArray<ManagedDefinitionEmission> ordered,
        IReadOnlyDictionary<EntityKey, StructuredMethod> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        InstructionModuleTarget target,
        IFunctionIndexResolver indices);

    ManagedBodyEmission[] Emit(
        ImmutableArray<ManagedMethodIdentity> ordered,
        IReadOnlyDictionary<string, MethodInstanceModel> instances,
        IReadOnlyDictionary<string, StructuredMethod> methods,
        IReadOnlyDictionary<string, MethodRootMap> rootMaps,
        InstructionModuleTarget target,
        IFunctionIndexResolver indices);
}

internal interface IManagedBodyBatchEmitterFactory
{
    IManagedBodyBatchEmitter Create();
}
