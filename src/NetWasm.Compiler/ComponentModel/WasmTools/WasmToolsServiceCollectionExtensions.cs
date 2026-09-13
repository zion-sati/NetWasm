using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.WasmTools;

internal static class WasmToolsServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelWasmTools(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWasmTools, ProcessWasmTools>();
        services.AddSingleton<IWasmTextModuleWriter, WasmTextModuleWriter>();
        services.AddSingleton<IEmscriptenEnvironmentShimWriter,
            EmscriptenEnvironmentShimWriter>();
        return services;
    }
}
