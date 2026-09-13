using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal static class MemoryInstructionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMemoryInstructions(
        this IServiceCollection services)
    {
        services.AddInstructionCommandProvider<ValueObjectBlockMemoryEmitter>();
        services.AddInstructionCommandProvider<FieldInstructionEmitter>();
        services.AddInstructionCommandProvider<StaticFieldInstructionEmitter>();
        services.AddInstructionCommandProvider<AtomicInstructionEmitter>();
        return services;
    }
}
