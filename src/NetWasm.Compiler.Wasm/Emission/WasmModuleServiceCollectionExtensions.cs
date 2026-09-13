using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Results;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class WasmModuleServiceCollectionExtensions
{
    public static IServiceCollection AddWasmModuleEmission(
        this IServiceCollection services,
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
        ILogger<WasmModuleEmitterFactory>? logger = null)
    {
        services.AddSingleton(types);
        services.AddSingleton(typeDefinitions);
        services.AddSingleton(fields);
        services.AddSingleton(methods);
        services.AddSingleton(symbols);
        services.AddSingleton(typeClassifier);
        services.AddSingleton(intrinsics);
        services.AddSingleton(targetLayout);
        services.AddSingleton(valueLayouts);
        services.AddSingleton(typeLayouts);
        services.AddSingleton(instanceFields);
        services.AddSingleton(staticFields);
        services.AddSingleton(staticData);
        services.AddSingleton(runtimeObjects);
        services.AddSingleton<IRectangularArrayLayoutProvider>(
            new RectangularArrayLayoutProvider(targetLayout, runtimeObjects));
        services.AddSingleton(exceptionObjects);
        services.AddSingleton(typeDescriptors);
        services.AddSingleton(typeDescriptors as IEnumMetadataSource ??
            throw new ArgumentException(
                "The type descriptor source must also provide enum metadata.",
                nameof(typeDescriptors)));
        services.AddSingleton(
            logger ?? NullLogger<WasmModuleEmitterFactory>.Instance);
        services.AddSingleton(targetLayout.Target);
        services.AddSingleton<IRuntimeImportResolver>(
            WasmRuntimeImports.CreateCatalog());
        services.AddStackTypeCompatibility();

        services
            .AddWasmEmissionSupport()
            .AddWasmPlanning()
            .AddWasmMethodEmission()
            .AddWasmEmissionResults()
            .AddWasmRuntimeIntrinsicEmission()
            .AddWasmCallEmission()
            .AddWasmInstructionEmission()
            .AddWasmJavaScriptInteropEmission()
            .AddWasmGeneratedFunctionEmission();

        services.AddSingleton<IWasmModuleBuilder>(WasmModuleBuilderFactory.Create());
        services.AddSingleton<IWasmModuleEmitter, WasmModuleEmitter>();
        return services;
    }
}
