using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Merging;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Linking;

internal static class LinkingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelLinking(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWasmOptimizationSettingsResolver, WasmOptimizationSettingsResolver>();
        services.AddSingleton<IManagedInliningHintsParser, ManagedInliningHintsParser>();
        services.AddSingleton<IManagedCompilerOptimizationOverridesParser,
            ManagedCompilerOptimizationOverridesParser>();
        services.AddSingleton<IWasmInlineAnnotationFilter, WasmInlineAnnotationFilter>();
        services.AddSingleton<IWasmCoreModuleInlineAnnotationEditor, WasmCoreModuleInlineAnnotationEditor>();
        services.AddSingleton<IWasmCoreModuleMerger, WasmCoreModuleMerger>();
        services.AddSingleton<IComponentCoreModuleMergeRunner, WasmCoreModuleMergeRunner>();
        services.AddSingleton<ComponentCoreModuleOptimizer>();
        services.AddSingleton<IComponentCoreModuleOptimizer>(provider => new ReportingComponentCoreModuleOptimizer(
            provider.GetRequiredService<ComponentCoreModuleOptimizer>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ReportingComponentCoreModuleOptimizer>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<INetWasmHostComponentShimWriter, PortableCommandHostComponentShimWriter>();
        services.AddSingleton<IManagedExecutableComponentAdapterWriter>(provider =>
            new ManagedExecutableComponentAdapterWriter(
            [
                new(ManagedExecutableCompletionShape.Synchronous,
                    new SynchronousManagedExecutableComponentAdapterWriter(
                        provider.GetRequiredService<IWasmTextModuleWriter>())),
                new(ManagedExecutableCompletionShape.Asynchronous,
                    new CallbackManagedExecutableComponentAdapterWriter(
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
