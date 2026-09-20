using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission;

public sealed record WasmManagedMethodEmissionMetric(
    string Identity,
    int WasmInstructionCount,
    int WasmBodyBytes,
    long CompileDurationTicks,
    long? PeakObservedManagedMemoryBytes,
    ImmutableDictionary<int, int> OriginalBlockEmissionCounts);

public sealed record WasmStackTraceSymbol(int Id, string Name);

public sealed record WasmModuleEmissionResult(
    byte[] Module,
    int StaticDataEnd,
    ImmutableArray<WasmManagedMethodEmissionMetric> ManagedMethodMetrics = default,
    ImmutableArray<WasmStackTraceSymbol> StackTraceSymbols = default)
{
    public ImmutableArray<WasmFunctionImport> FunctionImports { get; init; } = [];
    public ImmutableArray<string> RuntimeFeatures { get; init; } = [];
}

internal interface IWasmModuleEmitter
{
    WasmModuleEmissionResult Emit(WasmModuleTarget target);
}

public interface IWasmModuleEmitterFactory
{
    WasmModuleEmissionResult Emit(
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics,
        ITargetLayout targetLayout,
        IValueLayoutProvider valueLayouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        IStaticDataLayout staticData,
        IRuntimeObjectLayout runtimeObjects,
        IManagedExceptionObjectProvider exceptionObjects,
        ITypeDescriptorSource typeDescriptors,
        WasmEmissionRequest request);
}

public sealed class WasmModuleEmitterFactory(
    ILogger<WasmModuleEmitterFactory>? logger,
    IManagedLayoutForkSourceFactory? layoutForks,
    CompilerParallelism? parallelism) : IWasmModuleEmitterFactory
{
    // Keep the constructor signature shipped in 0.2.3 for existing consumers.
    public WasmModuleEmitterFactory(
        ILogger<WasmModuleEmitterFactory>? logger = null)
        : this(logger, null, null) { }

    public WasmModuleEmissionResult Emit(
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics,
        ITargetLayout targetLayout,
        IValueLayoutProvider valueLayouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        IStaticDataLayout staticData,
        IRuntimeObjectLayout runtimeObjects,
        IManagedExceptionObjectProvider exceptionObjects,
        ITypeDescriptorSource typeDescriptors,
        WasmEmissionRequest request)
    {
        var workerCount = OperatingSystem.IsBrowser()
            ? 1
            : parallelism?.WorkerCount ?? 1;
        var forks = workerCount > 1 && layoutForks is not null
            ? layoutForks.Create(targetLayout, types, typeDefinitions, fields)
            : null;
        return WasmModuleEmissionCompositionRoot.Emit(
            types,
            typeDefinitions,
            fields,
            methods,
            symbols,
            typeClassifier,
            intrinsics,
            targetLayout,
            valueLayouts,
            typeLayouts,
            instanceFields,
            staticFields,
            staticData,
            runtimeObjects,
            exceptionObjects,
            typeDescriptors,
            request,
            logger ?? NullLogger<WasmModuleEmitterFactory>.Instance,
            forks,
            workerCount);
    }
}
