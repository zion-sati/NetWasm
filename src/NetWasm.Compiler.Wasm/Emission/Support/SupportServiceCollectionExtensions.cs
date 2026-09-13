using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Support;

internal static class SupportServiceCollectionExtensions
{
    public static IServiceCollection AddWasmEmissionSupport(
        this IServiceCollection services)
    {
        services.AddSingleton<AddressInstructionEmitter>();
        services.AddSingleton<IAddressInstructionEmitter>(provider =>
            provider.GetRequiredService<AddressInstructionEmitter>());
        return services;
    }
}
