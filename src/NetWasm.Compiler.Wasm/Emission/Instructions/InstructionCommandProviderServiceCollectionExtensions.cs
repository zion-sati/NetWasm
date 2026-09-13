using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal static class InstructionCommandProviderServiceCollectionExtensions
{
    public static IServiceCollection AddInstructionCommandProvider<T>(
        this IServiceCollection services)
        where T : class, IInstructionCommandProvider
    {
        services.AddSingleton<T>();
        services.AddSingleton<IInstructionCommandProvider>(provider =>
            provider.GetRequiredService<T>());
        return services;
    }
}
