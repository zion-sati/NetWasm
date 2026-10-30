using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal static class MemoryInstructionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMemoryInstructions(
        this IServiceCollection services)
    {
        services.AddSingleton<IStaticInitializationEmitter, StaticInitializationEmitter>();
        services.AddSingleton<IMethodInitializationTriggerClassifier, MethodInitializationTriggerClassifier>();
        services.AddSingleton<IStaticFieldInitializationClassifier, StaticFieldInitializationClassifier>();
        services.AddInstructionCommandProvider<ValueObjectBlockMemoryEmitter>();
        services.AddInstructionCommandProvider<FieldInstructionEmitter>();
        services.AddInstructionCommandProvider<StaticFieldInstructionEmitter>();
        services.AddInstructionCommandProvider<ReadOnlySpanInstructionEmitter>();
        services.AddInstructionCommandProvider<StaticDataElementInstructionEmitter>();
        services.AddInstructionCommandProvider<AtomicInstructionEmitter>();
        return services;
    }
}
