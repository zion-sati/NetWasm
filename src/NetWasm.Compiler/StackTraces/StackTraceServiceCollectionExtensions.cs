using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.StackTraces;

internal static class StackTraceServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerStackTraces(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStackTraceReachabilityRootProvider,
            StackTraceReachabilityRootProvider>();
        services.AddSingleton<IStackTraceSymbolWriter, StackTraceSymbolWriter>();
        services.AddSingleton<ICompilationStackTraceArtifactBinder,
            CompilationStackTraceArtifactBinder>();
        return services;
    }
}
