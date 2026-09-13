using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.Delegates;

internal static class DelegateAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddDelegateAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDelegateBindingPlannerFactory,
            DelegateBindingPlannerFactory>();
        return services;
    }
}
