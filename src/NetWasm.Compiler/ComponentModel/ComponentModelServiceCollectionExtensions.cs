using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.FileSystem;
using NetWasm.Compiler.ComponentModel.Linking;
using NetWasm.Compiler.ComponentModel.Packaging;
using NetWasm.Compiler.ComponentModel.Process;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.WasmTools;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

internal static class ComponentModelServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModel(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCompilerComponentModelFileSystem();
        services.AddCompilerComponentModelProcess();
        services.AddCompilerComponentModelWasmTools();
        services.AddCompilerComponentModelLinking();
        services.AddCompilerRawModuleLinking();
        services.AddCompilerRawBindings();
        services.AddSingleton<Functions.IWitCanonicalFunctionBuilder, Functions.WitCanonicalFunctionBuilder>();
        services.AddSingleton<IWitDocumentJsonReader, WitDocumentJsonReader>();
        services.AddSingleton<IWitCoreBindingResolver, WitCoreBindingResolver>();
        services.AddSingleton<IWitDocumentReader, WitDocumentReader>();
        services.AddSingleton<IWitCanonicalTypeResolver, WitCanonicalTypeResolver>();
        services.AddSingleton<IWitInterfaceSpecifierFormatter, WitInterfaceSpecifierFormatter>();
        services.AddSingleton<IWitTypeIdentityFormatter, WitTypeIdentityFormatter>();
        services.AddSingleton<IWitJavaScriptNameFormatter, WitJavaScriptNameFormatter>();
        services.AddSingleton<IWitWorkerTypeReferenceReader>(static _ =>
            new WitWorkerTypeReferenceReader(WitWorkerReferenceComposition.CreateReaders()));
        services.AddSingleton<IWitWorkerTypeProjector, WitWorkerTypeProjector>();
        services.AddSingleton<IWitInterfaceCatalogBuilder, WitInterfaceCatalogBuilder>();
        services.AddSingleton<IWitWorldFunctionProjector, WitWorldFunctionProjector>();
        services.AddSingleton<ICanonicalAbiTypeFlattener, CanonicalAbiTypeFlattener>();
        services.AddSingleton<ICanonicalAbiSignaturePlanner, CanonicalAbiSignaturePlanner>();
        services.AddSingleton<ICanonicalAbiMemoryLayoutPlanner,
            CanonicalAbiMemoryLayoutPlanner>();
        services.AddSingleton<IWitWorldValidator, WitWorldValidator>();
        services.AddCompilerComponentModelPackaging();
        return services;
    }
}
