using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Interop;

internal static class InteropServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerInterop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IInteropDeclarationValidator,
            InteropDeclarationValidator>();
        services.AddSingleton<IHostInteropManifestBuilder,
            HostInteropManifestBuilder>();
        services.AddSingleton<IComponentContractResolver,
            ComponentContractResolver>();
        services.AddSingleton<IWitManagedBindingSelector,
            WitManagedBindingSelector>();
        services.AddSingleton<IJavaScriptAsyncBindingResolverFactory,
            JavaScriptAsyncBindingResolverFactory>();
        return services;
    }
}
