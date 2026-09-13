using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

internal static class ManagedCallSiteResolutionServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCallSiteResolution(this IServiceCollection services)
    {
        services.AddSingleton<IManagedMethodIdentityFactory, ManagedMethodIdentityFactory>();
        services.AddSingleton<IManagedCallSiteResolver, ManagedCallSiteResolver>();
        return services;
    }
}
