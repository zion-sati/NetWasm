using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

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
    IMemberDescriptorLayout memberDescriptors,
    IRuntimeObjectLayout runtimeObjects,
    IManagedExceptionObjectProvider exceptionObjects,
    ITypeDescriptorSource typeDescriptors,
    ILogger<WasmModuleEmitterFactory> logger,
    CompiledUnitState? units = null)
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
    public IMemberDescriptorLayout MemberDescriptors { get; } = memberDescriptors;
    public IRuntimeObjectLayout RuntimeObjects { get; } = runtimeObjects;
    public IManagedExceptionObjectProvider ExceptionObjects { get; } = exceptionObjects;
    public ITypeDescriptorSource TypeDescriptors { get; } = typeDescriptors;
    public ILogger<WasmModuleEmitterFactory> Logger { get; } = logger;
    // The compile's compiled units; each worker restores and records against them.
    public CompiledUnitState? Units { get; } = units?.Directory is null ? null : units;
    // Structures outlined methods when they are emitted after all.
    public IWasmMethodLowerer? Structuring { get; init; } = units?.Structuring;
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
        var dependencies = shared.Units is null ? null : new UnitDependencyLog();
        ICompiledUnitRestorer? restorer = null;
        var registrations = new ServiceCollection();
        if (dependencies is null)
        {
            registrations.AddWasmModuleEmission(
                shared.Types, shared.TypeDefinitions, shared.Fields,
                shared.Methods, shared.Symbols, shared.TypeClassifier,
                shared.Intrinsics, shared.TargetLayout, fork.Values,
                shared.TypeLayouts, fork.Fields, shared.StaticFields,
                shared.StaticData, shared.MemberDescriptors, shared.RuntimeObjects,
                shared.ExceptionObjects, shared.TypeDescriptors, logger);
        }
        else
        {
            var values = new RecordingValueLayoutProvider(fork.Values, dependencies);
            var types = new RecordingTypeLayoutProvider(shared.TypeLayouts, dependencies);
            var fields = new RecordingInstanceFieldLayoutProvider(fork.Fields, dependencies);
            var statics = new RecordingStaticFieldLayoutProvider(shared.StaticFields, dependencies);
            var data = new RecordingStaticDataLayout(shared.StaticData, dependencies);
            var descriptors = new RecordingMemberDescriptorLayout(shared.MemberDescriptors, dependencies);
            var exceptions = new RecordingManagedExceptionObjectProvider(shared.ExceptionObjects, dependencies);
            var typeDescriptors = new RecordingTypeDescriptorSource(shared.TypeDescriptors, dependencies);
            registrations.AddWasmModuleEmission(
                shared.Types, shared.TypeDefinitions, shared.Fields,
                shared.Methods, shared.Symbols, shared.TypeClassifier,
                shared.Intrinsics, shared.TargetLayout, values, types, fields, statics,
                data, descriptors, shared.RuntimeObjects, exceptions, typeDescriptors, logger);
            restorer = new CompiledUnitRestorer(shared.Units!,
                new UnitQueryReplayer(dependencies, asking =>
                    {
                        dependencies.Begin();
                        try
                        {
                            asking();
                        }
                        finally
                        {
                            _ = dependencies.End();
                        }
                        return dependencies.Answers;
                    }, types, values, fields, statics, data, descriptors, exceptions, typeDescriptors,
                    shared.TypeDefinitions, shared.Fields),
                new WasmIndexOperandRewriter());
        }
        if (shared.Structuring is { } structuring)
            registrations.AddSingleton(structuring);
        var provider = registrations.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        var worker = Resolve(provider, provider, logger, dependencies);
        if (restorer is null) return worker;
        ManagedBodyReplay? replay = null;
        if (shared.Units!.Replay is { } unitReplay)
            replay = new(Beside(worker, () => Create(unitReplay.Capabilities(shared), unitReplay.Fork)), unitReplay);
        return new ManagedBodyWorker(provider, worker.Ordinary, worker.Constructed, worker.Logger)
        {
            Dependencies = dependencies,
            Restorer = restorer,
            VerifiesUnits = shared.Units.Verifies,
            Replay = replay,
        };
    }

    // What is built beside a worker; if it cannot be, the worker is released with it.
    internal static T Beside<T>(IDisposable worker, Func<T> build)
    {
        try
        {
            return build();
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    internal static ManagedBodyWorker Resolve(
        ServiceProvider provider,
        IDisposable owner,
        MethodBufferedLogger logger,
        UnitDependencyLog? dependencies = null)
    {
        try
        {
            return new(owner,
                provider.GetRequiredService<IManagedDefinitionFunctionAppender>(),
                provider.GetRequiredService<IConstructedMethodFunctionAppender>(),
                logger)
            {
                Dependencies = dependencies,
            };
        }
        catch
        {
            owner.Dispose();
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
    // Records the current method's queries when compiled units are on.
    public UnitDependencyLog? Dependencies { get; init; }
    // Restores a method from its compiled unit when the unit is still valid.
    public ICompiledUnitRestorer? Restorer { get; init; }
    // Emits restored methods as well, to prove their units sound.
    public bool VerifiesUnits { get; init; }
    // Emits each method again against the last compile's answers, to prove the replay sound.
    public ManagedBodyReplay? Replay { get; init; }

    public void Dispose()
    {
        Replay?.Worker.Dispose();
        provider.Dispose();
    }
}

/// <summary>A worker that emits against the last compile's answers, and that compile.</summary>
internal sealed record ManagedBodyReplay(ManagedBodyWorker Worker, UnitReplay Units);
