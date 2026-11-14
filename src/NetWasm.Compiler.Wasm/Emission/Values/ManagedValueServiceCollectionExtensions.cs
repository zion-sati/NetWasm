using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal static class ManagedValueServiceCollectionExtensions
{
    public static IServiceCollection AddManagedValuePlanning(
        this IServiceCollection services)
    {
        services.AddSingleton<IRootSlotAllocator, RootSlotAllocator>();
        services.AddSingleton<IManagedAggregateStorageFinder, ManagedAggregateStorageFinder>();
        services.AddSingleton<IManagedAggregateRootObligationRecorder, ManagedAggregateRootObligationRecorder>();
        services.AddSingleton<IManagedAggregateFieldResolver, ManagedAggregateFieldResolver>();
        services.AddSingleton<IManagedAggregateFieldAddressResolver, ManagedAggregateFieldAddressResolver>();
        services.AddSingleton<IManagedAggregateFieldAccessRecorder, ManagedAggregateFieldAccessRecorder>();
        services.AddSingleton<IManagedAggregateObjectAccessRecorder, ManagedAggregateObjectAccessRecorder>();
        services.AddSingleton<IManagedAggregateClearRecorder, ManagedAggregateClearRecorder>();
        services.AddSingleton<IManagedAggregateCallEffectApplier, ManagedAggregateCallEffectApplier>();
        services.AddSingleton<IManagedAggregateInstructionTransfer, ManagedAggregateInstructionTransfer>();
        services.AddSingleton<IManagedAggregateBlockTransfer, ManagedAggregateBlockTransfer>();
        services.AddSingleton<IStructuredLoopDepthMeasurer, StructuredLoopDepthMeasurer>();
        services.AddSingleton<IUnamortizedAggregateStoreRejector, UnamortizedAggregateStoreRejector>();
        services.AddSingleton<IManagedAggregateValueAnalyzer,
            ManagedAggregateValueAnalyzer>();
        services.AddSingleton<IManagedAggregateSsaPlanner,
            ManagedAggregateSsaPlanner>();
        services.AddSingleton<IManagedAggregateRootMapProjector,
            ManagedAggregateRootMapProjector>();
        services.AddSingleton<IManagedMethodValuePlanBuilder,
            ManagedMethodValuePlanBuilder>();
        services.AddSingleton<IManagedAggregateValueInitializer,
            ManagedAggregateValueInitializer>();
        services.AddSingleton<IManagedAggregateValueInstructionEmitter,
            ManagedAggregateValueInstructionEmitter>();
        services.AddSingleton<IManagedAggregateValueReloadEmitter,
            ManagedAggregateValueReloadEmitter>();
        return services;
    }
}
