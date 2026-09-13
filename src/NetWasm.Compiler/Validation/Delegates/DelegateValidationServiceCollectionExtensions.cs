using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Validation.Delegates;

internal static class DelegateValidationServiceCollectionExtensions
{
    public static IServiceCollection AddDelegateValidation(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDelegateBindingInvariantValidator,
            DelegateBindingInvariantValidator>();
        return services;
    }
}
