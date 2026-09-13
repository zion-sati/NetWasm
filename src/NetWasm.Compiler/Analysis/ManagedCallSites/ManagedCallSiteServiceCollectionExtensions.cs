using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.ManagedCallSites;

internal static class ManagedCallSiteServiceCollectionExtensions
{
    public static IServiceCollection AddManagedCallSiteAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IManagedCallSiteLedgerWriter, ManagedCallSiteLedgerWriter>();
        return services;
    }
}
