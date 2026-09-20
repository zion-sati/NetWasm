using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedBodySharedCapabilities(
    ITypeRepository types,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IMethodRepository methods,
    ISymbolFormatter symbols,
    ITypeClassifier typeClassifier,
    IRuntimeIntrinsicRegistry intrinsics,
    ITargetLayout targetLayout,
    ITypeLayoutProvider typeLayouts,
    IStaticFieldLayoutProvider staticFields,
    IStaticDataLayout staticData,
    IRuntimeObjectLayout runtimeObjects,
    IManagedExceptionObjectProvider exceptionObjects,
    ITypeDescriptorSource typeDescriptors,
    ILogger<WasmModuleEmitterFactory> logger)
{
    public ITypeRepository Types { get; } = types;
    public ITypeDefinitionResolver TypeDefinitions { get; } = typeDefinitions;
    public IFieldRepository Fields { get; } = fields;
    public IMethodRepository Methods { get; } = methods;
    public ISymbolFormatter Symbols { get; } = symbols;
    public ITypeClassifier TypeClassifier { get; } = typeClassifier;
    public IRuntimeIntrinsicRegistry Intrinsics { get; } = intrinsics;
    public ITargetLayout TargetLayout { get; } = targetLayout;
    public ITypeLayoutProvider TypeLayouts { get; } = typeLayouts;
    public IStaticFieldLayoutProvider StaticFields { get; } = staticFields;
    public IStaticDataLayout StaticData { get; } = staticData;
    public IRuntimeObjectLayout RuntimeObjects { get; } = runtimeObjects;
    public IManagedExceptionObjectProvider ExceptionObjects { get; } = exceptionObjects;
    public ITypeDescriptorSource TypeDescriptors { get; } = typeDescriptors;
    public ILogger<WasmModuleEmitterFactory> Logger { get; } = logger;
}

internal interface IManagedBodyWorkerFactory
{
    ManagedBodyWorker Create(ManagedLayoutFork fork);
}

internal sealed class ManagedBodyWorkerFactory(
    ManagedBodySharedCapabilities shared) : IManagedBodyWorkerFactory
{
    public ManagedBodyWorker Create(ManagedLayoutFork fork) =>
        ManagedBodyWorkerCompositionRoot.Create(shared, fork);
}

/// <summary>Composes one private body-emission graph from supplied capabilities.</summary>
internal static class ManagedBodyWorkerCompositionRoot
{
    internal static ManagedBodyWorker Create(
        ManagedBodySharedCapabilities shared, ManagedLayoutFork fork)
    {
        ArgumentNullException.ThrowIfNull(fork);
        var logger = new MethodBufferedLogger(shared.Logger);
        var registrations = new ServiceCollection();
        registrations.AddWasmModuleEmission(
            shared.Types, shared.TypeDefinitions, shared.Fields,
            shared.Methods, shared.Symbols, shared.TypeClassifier,
            shared.Intrinsics, shared.TargetLayout, fork.Values,
            shared.TypeLayouts, fork.Fields, shared.StaticFields,
            shared.StaticData, shared.RuntimeObjects,
            shared.ExceptionObjects, shared.TypeDescriptors, logger);
        var provider = registrations.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        try
        {
            return new(provider,
                provider.GetRequiredService<IManagedDefinitionFunctionAppender>(),
                provider.GetRequiredService<IConstructedMethodFunctionAppender>(),
                logger);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }
}

internal sealed class ManagedBodyWorker(
    IDisposable provider,
    IManagedDefinitionFunctionAppender ordinary,
    IConstructedMethodFunctionAppender constructed,
    MethodBufferedLogger logger) : IDisposable
{
    public IManagedDefinitionFunctionAppender Ordinary { get; } = ordinary;
    public IConstructedMethodFunctionAppender Constructed { get; } = constructed;
    public MethodBufferedLogger Logger { get; } = logger;

    public void Dispose() => provider.Dispose();
}
