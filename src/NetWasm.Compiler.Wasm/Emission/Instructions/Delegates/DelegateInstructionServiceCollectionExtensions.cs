using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Delegates;

internal static class DelegateInstructionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmDelegateInstructions(
        this IServiceCollection services)
    {
        services.AddInstructionCommandProvider<DelegateInstructionEmitter>();
        return services;
    }
}
