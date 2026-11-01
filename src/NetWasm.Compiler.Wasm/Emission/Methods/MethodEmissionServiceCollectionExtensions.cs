using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class MethodEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMethodEmission(
        this IServiceCollection services)
    {
        services.AddAddressTakenValuePromotion();
        services.AddManagedValuePlanning();
        services.AddArrayMetadataCaching();
        services.AddScalarCallPreservation();
        services.AddSingleton<IManagedDefinitionFunctionAppender,
            ManagedDefinitionFunctionAppender>();
        services.AddSingleton<IManagedDefinitionSetAppender,
            ManagedDefinitionSetAppender>();
        services.AddSingleton<IConstructedMethodFunctionAppender,
            ConstructedMethodFunctionAppender>();
        services.AddSingleton<IConstructedMethodSetAppender,
            ConstructedMethodSetAppender>();
        services.AddSingleton<IManagedMethodEmissionMetricProjector,
            ManagedMethodEmissionMetricProjector>();
        services.AddSingleton<IManagedHeapSampler, ManagedHeapSampler>();
        services.AddSingleton<IExceptionGroupEnumerator, ExceptionGroupEnumerator>();
        services.AddSingleton<IFilterEmissionCountMerger,
            FilterEmissionCountMerger>();
        services.AddSingleton<IManagedMethodFunctionTypeResolver,
            ManagedMethodFunctionTypeResolver>();
        services.AddSingleton<IDirectValueLoadSelector,
            DirectValueLoadSelector>();
        services.AddSingleton<ITransientAggregatePromotionPlanner,
            TransientAggregatePromotionPlanner>();
        services.AddSingleton<IValueFrameLayoutPlanner, ValueFrameLayoutPlanner>();
        services.AddSingleton<IValueFrameReferenceMapPlanner, ValueFrameReferenceMapPlanner>();
        services.AddSingleton<FilterEnvironmentLayoutPlanner>();
        services.AddSingleton<IFilterEnvironmentLayoutPlanner>(provider =>
            provider.GetRequiredService<FilterEnvironmentLayoutPlanner>());
        services.AddSingleton<ValueFrameAddressEmitter>();
        services.AddSingleton<IValueFrameAddressEmitter>(provider =>
            provider.GetRequiredService<ValueFrameAddressEmitter>());
        services.AddSingleton<IValueFrameInitializationEmitter,
            ValueFrameInitializationEmitter>();
        services.AddSingleton<IFilterEnvironmentRootEmitter,
            FilterEnvironmentRootEmitter>();
        services.AddSingleton<IStackTraceFrameEntryEmitter,
            StackTraceFrameEntryEmitter>();
        services.AddSingleton<IStackTraceFrameLocationEmitter,
            StackTraceFrameLocationEmitter>();
        services.AddSingleton<IStackTraceFrameExitEmitter,
            StackTraceFrameExitEmitter>();
        services.AddSingleton<IMethodFrameEntryEmitter, MethodFrameEntryEmitter>();
        services.AddSingleton<IValueFrameEntryEmitter, ValueFrameEntryEmitter>();
        services.AddSingleton<ITerminalBranchValueFramePlanner, TerminalBranchValueFramePlanner>();
        services.AddSingleton<ITerminalBranchValueFrameEmitter, TerminalBranchValueFrameEmitter>();
        services.AddSingleton<IMethodFrameExitEmitter, MethodFrameExitEmitter>();
        services.AddSingleton<ManagedMethodEmitter>();
        services.AddSingleton<IAuthoritativeRootSourceSelector, AuthoritativeRootSourceSelector>();
        services.AddSingleton<IAuthoritativeRootLayoutPlanner, AuthoritativeRootLayoutPlanner>();
        services.AddSingleton<IRootClearElisionPlanner, RootClearElisionPlanner>();
        services.AddSingleton<IRootValueElisionPlanner, RootValueElisionPlanner>();
        services.AddSingleton<IInstructionCountingWriterFactory,
            InstructionCountingWriterFactory>();
        services.AddSingleton<IManagedMethodEmitter>(provider =>
            provider.GetRequiredService<ManagedMethodEmitter>());
        services.AddSingleton<ExceptionRegionEmitter>();
        services.AddSingleton<IExceptionRegionEmitter>(provider =>
            provider.GetRequiredService<ExceptionRegionEmitter>());
        services.AddSingleton<IDispatcherBranchTablePlanner,
            DispatcherBranchTablePlanner>();
        services.AddSingleton<IControlFlowDispatcherEmitter,
            ControlFlowDispatcherEmitter>();
        services.AddSingleton<StructuredControlFlowEmitter>();
        services.AddSingleton<IStructuredConditionValueEmitter, StructuredConditionValueEmitter>();
        services.AddSingleton<IStructuredControlFlowEmitter>(provider =>
            provider.GetRequiredService<StructuredControlFlowEmitter>());
        services.AddSingleton<IStructuredLeaveEmitter, StructuredLeaveEmitter>();
        services.AddSingleton<IBranchComparisonEmitter, BranchComparisonEmitter>();
        services.AddSingleton<IManagedMethodSequenceEmitter,
            ManagedMethodSequenceEmitter>();
        services.AddSingleton<IManagedMethodBodyEmitterFactory,
            ManagedMethodBodyEmitterFactory>();
        services.AddSingleton<IManagedMethodBodyEmitter>(provider =>
            provider.GetRequiredService<IManagedMethodBodyEmitterFactory>().Create());
        return services;
    }
}
