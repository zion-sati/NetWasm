using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ControlFlow;

public static class StackTypeCompatibilityServiceCollectionExtensions
{
    public static IServiceCollection AddStackTypeCompatibility(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStackTypeCompatibilityValidator,
            StackTypeCompatibilityValidator>();
        return services;
    }
}
