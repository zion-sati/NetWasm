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
    long PeakObservedManagedMemoryBytes,
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
    ILogger<WasmModuleEmitterFactory>? logger = null) : IWasmModuleEmitterFactory
{
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
        WasmEmissionRequest request) => WasmModuleEmissionCompositionRoot.Emit(
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
            logger ?? NullLogger<WasmModuleEmitterFactory>.Instance);
}
