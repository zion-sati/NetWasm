using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.EntryPoints;

internal static class EntryPointServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerEntryPoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEntryPointValidationStrategy,
            RawFunctionEntryPointValidationStrategy>();
        services.AddSingleton<IEntryPointValidationStrategy,
            ManagedExecutableEntryPointValidationStrategy>();
        services.AddSingleton<IEntryPointValidator, EntryPointValidator>();
        services.AddSingleton<IEntryPointSelector, EntryPointSelector>();
        services.AddSingleton<ICompilationExportResolver, RequestedExportResolver>();
        services.AddSingleton<ICompilationExportResolver, JavaScriptExportResolver>();
        services.AddSingleton<ICompilationExportResolver, WitExportResolver>();
        services.AddSingleton<ICompilationExportResolver,
            WitPostReturnExportResolver>();
        services.AddSingleton<ICompilationExportNameValidator,
            CompilationExportNameValidator>();
        services.AddSingleton<ICompilationExportsResolver, CompilationExportResolver>();
        services.AddSingleton<ICompilationEntryResolver, CompilationEntryResolver>();
        services.AddSingleton<IManagedExecutableArgumentFactoryResolver,
            ManagedExecutableArgumentFactoryResolver>();
        return services;
    }
}
