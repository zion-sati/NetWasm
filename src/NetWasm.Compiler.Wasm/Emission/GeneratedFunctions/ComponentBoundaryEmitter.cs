using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record ComponentBoundaryArtifacts(
    ImmutableArray<WasmFunctionDefinition> Functions,
    ImmutableArray<WasmExport> Exports,
    ImmutableArray<string> RuntimeFeatures);

internal sealed class ComponentBoundaryEmitter(
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeState,
    ICanonicalAbiFunctionTypePlanner canonicalTypes,
    IStartupPreflightCallEmitter startupPreflights,
    IStartupPreflightMethodSelector startupMethods,
    IGeneratedFunctionWriterFactory writers) : IComponentBoundaryEmitter
{
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports;
    private readonly IRuntimeStateInitializer _runtimeState = runtimeState;
    private readonly ICanonicalAbiFunctionTypePlanner _canonicalTypes = canonicalTypes;
    private readonly IStartupPreflightCallEmitter _startupPreflights =
        startupPreflights;
    private readonly IStartupPreflightMethodSelector _startupMethods = startupMethods;

    public ComponentBoundaryArtifacts Emit(
        WasmEmissionRequest request,
        WasmTarget target,
        RuntimeInitializationPlan initialization,
        int importedFunctionCount,
        int definedFunctionCount,
        IReadOnlyDictionary<string, int> managedExportIndices,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(managedExportIndices);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var witExports = request.ComponentContract.Exports
            .OrderBy(function => function.InterfaceName, StringComparer.Ordinal)
            .ThenBy(function => function.FunctionName, StringComparer.Ordinal)
            .ToArray();
        if (request.ComponentContract.IsEmpty)
        {
            return new([], [], []);
        }

        var functions = ImmutableArray.CreateBuilder<WasmFunctionDefinition>();
        var exports = ImmutableArray.CreateBuilder<WasmExport>();
        exports.Add(new(CanonicalAbiNames.Memory(target), 0, WasmExportKind.Memory));

        AddFunction(
            "component.realloc",
            WasmFunctionType.Create(
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress),
            EmitReallocate(),
            CanonicalAbiNames.Reallocate(target));
        var initialize = EmitInitialize(request, initialization, functionIndices);
        AddFunction(
            "component.initialize",
            WasmFunctionType.Create(CliValueKind.Void),
            initialize.Body,
            CanonicalAbiNames.Initialize(target));

        foreach (var function in witExports)
        {
            var exportName = CanonicalAbiNames.Export(
                function,
                target);
            if (!managedExportIndices.ContainsKey(exportName))
            {
                throw new InvalidOperationException(
                    $"component export '{exportName}' has no managed wrapper");
            }
            if (function.Kind == CanonicalAbiFunctionKind.ExportedResourceDestructor)
            {
                continue;
            }
            var type = _canonicalTypes.Plan(
                function,
                CanonicalAbiDirection.LiftedExport).PostReturnType;
            var postReturnName = CanonicalAbiNames.PostReturn(function, target);
            if (managedExportIndices.ContainsKey(postReturnName))
            {
                continue;
            }
            AddFunction(
                $"component.post-return.{exportName}",
                type,
                EmitNoOp(),
                postReturnName);
        }

        return new(
            functions.ToImmutable(),
            exports.ToImmutable(),
            initialize.UsesLocalTime ? [NetWasmRuntimeFeatureIds.LocalTime] : []);

        void AddFunction(
            string name,
            WasmFunctionType type,
            byte[] body,
            string exportName)
        {
            var index = importedFunctionCount + definedFunctionCount + functions.Count;
            functions.Add(new(name, type, body));
            exports.Add(new(exportName, index));
        }
    }

    private byte[] EmitReallocate()
    {
        var body = writers.Create();
        body.Bytes.Write([0]);
        for (var index = 0; index < 4; index++)
        {
            body.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)index)));
        }
        body.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(
                RuntimeImportSymbol.ComponentReallocate))));
        body.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return body.Snapshots.Read();
    }

    private (byte[] Body, bool UsesLocalTime) EmitInitialize(
        WasmEmissionRequest request,
        RuntimeInitializationPlan initialization,
        IFunctionIndexResolver functionIndices)
    {
        var body = writers.Create();
        body.Bytes.Write([0]);
        _runtimeState.Initialize(body, initialization);
        var preflights = _startupMethods.Select(request);
        if (!StartupPreflightPlacement.RunsInProcessEntry(request))
        {
            _startupPreflights.Emit(body, preflights.Methods, functionIndices);
        }
        body.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return (body.Snapshots.Read(), preflights.UsesLocalTime);
    }

    private byte[] EmitNoOp()
    {
        var body = writers.Create();
        body.Bytes.Write([0]);
        body.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return body.Snapshots.Read();
    }
}
