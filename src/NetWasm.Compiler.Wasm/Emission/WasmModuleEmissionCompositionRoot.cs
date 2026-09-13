using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
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
        ILogger<WasmModuleEmitterFactory> logger)
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
