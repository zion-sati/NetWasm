using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.Raw;

internal static class RawModuleLinkingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerRawModuleLinking(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRawModuleLinkInputValidator, RawModuleLinkInputValidator>();
        services.AddSingleton<IRawModuleLinkExecution, RawModuleLinkExecution>();
        services.AddSingleton<IRawModuleLinker, RawModuleLinker>();
        return services;
    }
}
