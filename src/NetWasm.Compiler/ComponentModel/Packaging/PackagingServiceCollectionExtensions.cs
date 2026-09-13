using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Packaging;

internal static class PackagingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelPackaging(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IComponentManifestBuilder, ComponentManifestBuilder>();
        services.AddSingleton<IComponentPackageOperationRunner,
            ComponentPackageOperationRunner>();
        services.AddSingleton<IComponentPackageInputValidator,
            ComponentPackageInputValidator>();
        services.AddSingleton<IComponentPackageWorkspaceFactory,
            ComponentPackageWorkspaceFactory>();
        services.AddSingleton<IComponentPackageExecution,
            ComponentPackageExecution>();
        services.AddSingleton<IComponentPackagingCapability,
            ComponentPackagingCapability>();
        services.AddSingleton<IComponentPackager, ComponentPackager>();
        services.AddSingleton<IWitWorldSpecifierFormatter,
            WitWorldSpecifierFormatter>();
        services.AddSingleton<IComponentBuilder, ComponentBuilder>();
        return services;
    }
}
