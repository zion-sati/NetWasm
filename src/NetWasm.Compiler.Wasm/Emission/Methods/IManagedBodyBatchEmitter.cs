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
    ManagedMethodEmissionRecord Record)
{
    // The queries the body's emission made, when compiled units are on.
    public ImmutableArray<NetWasm.Compiler.Wasm.Units.UnitDependency> Dependencies { get; init; } = [];
    // Whether the body was restored from its compiled unit rather than emitted.
    public bool IsRestored => Unit is not null;
    // Where the unit the body was restored from is; it is kept there for the next compile.
    public NetWasm.Compiler.Wasm.Units.UnitLocation? Unit { get; init; }
}

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
