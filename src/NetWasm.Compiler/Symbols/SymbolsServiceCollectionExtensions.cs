using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Symbols;

internal static class SymbolsServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerSymbols(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRuntimeTypeSymbolTableBuilder, RuntimeTypeSymbolTableBuilder>();
        services.AddSingleton<ISymbolSidecarWriter, SymbolSidecarWriter>();
        services.AddSingleton<ICompilationSymbolSidecarBinder, CompilationSymbolSidecarBinder>();
        return services;
    }
}
