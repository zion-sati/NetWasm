using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission;

/// <summary>
/// Explicit composition root for an emission graph whose repository and layout
/// capabilities are supplied by the compiler pipeline for one module.
/// </summary>
internal static class WasmModuleEmissionCompositionRoot
{
    public static WasmModuleEmissionResult Emit(
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
        WasmEmissionRequest request,
        ILogger<WasmModuleEmitterFactory> logger,
        IManagedLayoutForkSource? layoutForks,
        int workerCount)
    {
        var services = new ServiceCollection();
        services.AddWasmModuleEmission(
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
            logger);
        if (layoutForks is not null && workerCount > 1)
        {
            var shared = new ManagedBodySharedCapabilities(
                types, typeDefinitions, fields, methods, symbols,
                typeClassifier, intrinsics, targetLayout, typeLayouts,
                staticFields, staticData, runtimeObjects, exceptionObjects,
                typeDescriptors, logger);
            services.AddSingleton<IManagedBodyWorkerFactory>(
                new ManagedBodyWorkerFactory(shared));
            services.AddSingleton<IManagedBodyBatchEmitterFactory>(provider =>
                new ManagedBodyBatchEmitterFactory(layoutForks, workerCount,
                    provider.GetRequiredService<IManagedBodyWorkerFactory>()));
            services.Replace(ServiceDescriptor.Singleton<
                IManagedDefinitionSetAppender,
                ParallelManagedDefinitionSetAppender>());
            services.Replace(ServiceDescriptor.Singleton<
                IConstructedMethodSetAppender,
                ParallelConstructedMethodSetAppender>());
        }
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var target = provider.GetRequiredService<IWasmModuleTargetFactory>()
            .Create(request);
        return provider.GetRequiredService<IWasmModuleEmitter>().Emit(target);
    }
}
