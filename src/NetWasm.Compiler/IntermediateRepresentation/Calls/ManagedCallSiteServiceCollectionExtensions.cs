using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.IntermediateRepresentation.Calls;

internal static class ManagedCallSiteServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCallSites(this IServiceCollection services)
    {
        services.AddSingleton<IManagedMethodIdentityFactory, ManagedMethodIdentityFactory>();
        services.AddSingleton<IManagedCallSiteFactory, ManagedCallSiteFactory>();
        return services;
    }
}
