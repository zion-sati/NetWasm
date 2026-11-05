using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>Emits single methods against a replayed last compile.</summary>
internal interface IReplayMethodEmitter : IDisposable
{
    // An ordinary method's function, emitted as the last compile's module would hold it.
    WasmFunctionDefinition Emit(ManagedMethodIdentity identity, EntityKey method, StructuredMethod structured,
        MethodRootMap rootMap);
}

internal interface IReplayMethodEmitterFactory
{
    IReplayMethodEmitter Create(UnitReplay replay, ITypeRepository types, ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields, IMethodRepository methods, ISymbolFormatter symbols, ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics, WasmTarget target);
}

internal sealed class ReplayMethodEmitterFactory(ILogger<WasmModuleEmitterFactory> logger) : IReplayMethodEmitterFactory
{
    public IReplayMethodEmitter Create(UnitReplay replay, ITypeRepository types, ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields, IMethodRepository methods, ISymbolFormatter symbols, ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var capabilities = replay.Capabilities(types, typeDefinitions, fields, methods, symbols, typeClassifier, intrinsics,
            new UnitTargetLayout(WasmTargetLayout.For(target)), logger, structuring: null);
        return new ReplayMethodEmitter(replay, ManagedBodyWorkerCompositionRoot.Create(capabilities, replay.Fork));
    }
}

internal sealed class ReplayMethodEmitter(UnitReplay replay, ManagedBodyWorker worker) : IReplayMethodEmitter
{
    public WasmFunctionDefinition Emit(ManagedMethodIdentity identity, EntityKey method, StructuredMethod structured,
        MethodRootMap rootMap)
    {
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        worker.Logger.BeginMethod(0);
        try
        {
            worker.Ordinary.Append(functions, environments, emissions, identity, method, structured, rootMap,
                replay.Target, replay.Indices);
        }
        finally
        {
            worker.Logger.EndMethod();
        }
        return functions.Single();
    }

    public void Dispose() => worker.Dispose();
}
