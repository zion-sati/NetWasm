using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.GarbageCollection;

internal static class GarbageCollectionServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerGarbageCollection(
        this IServiceCollection services)
    {
        services.AddSingleton<IRootMapAnalyzerFactory, RootMapAnalyzerFactory>();
        services.AddSingleton<IRootSlotAllocator, RootSlotAllocator>();
        services.AddSingleton<IRootDecisionClassifierFactory,
            RootDecisionClassifierFactory>();
        services.AddSingleton<ICalleeArgumentRootingClassifierFactory,
            CalleeArgumentRootingClassifierFactory>();
        services.AddSingleton<IRuntimeAllocationSafepointClassifier,
            RuntimeAllocationSafepointClassifier>();
        return services;
    }
}
