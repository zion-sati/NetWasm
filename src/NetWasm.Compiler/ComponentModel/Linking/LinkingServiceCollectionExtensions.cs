using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Linking;

internal static class LinkingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelLinking(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IComponentCoreModuleMergeRunner,
            ComponentCoreModuleMergeRunner>();
        services.AddSingleton<IWasmCoreModuleExportEditor,
            WasmCoreModuleExportEditor>();
        services.AddSingleton<IComponentCoreModuleOptimizer,
            ComponentCoreModuleOptimizer>();
        services.AddSingleton<INetWasmHostComponentShimWriter,
            NetWasmHostComponentShimWriter>();
        services.AddSingleton<IManagedExecutableComponentAdapterWriter>(provider =>
            new ManagedExecutableComponentAdapterWriter(
            [
                new(ManagedExecutableCompletionShape.Synchronous,
                    new SynchronousManagedExecutableComponentAdapterWriter(
                        provider.GetRequiredService<IWasmTextModuleWriter>())),
                new(ManagedExecutableCompletionShape.Asynchronous,
                    new AsynchronousManagedExecutableComponentAdapterWriter(
                        provider.GetRequiredService<IWasmTextModuleWriter>())),
            ]));
        services.AddSingleton<IComponentCoreModuleLinker,
            ComponentCoreModuleLinker>();
        services.AddSingleton<IComponentCoreModuleInputValidator,
            ComponentCoreModuleInputValidator>();
        services.AddSingleton<IComponentCoreModuleWorkspaceFactory,
            ComponentCoreModuleWorkspaceFactory>();
        services.AddSingleton<IComponentCoreModuleLinkExecution,
            ComponentCoreModuleLinkExecution>();
        return services;
    }
}
