using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class MethodEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMethodEmission(
        this IServiceCollection services)
    {
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
        services.AddSingleton<IExceptionGroupEnumerator, ExceptionGroupEnumerator>();
        services.AddSingleton<IFilterEmissionCountMerger,
            FilterEmissionCountMerger>();
        services.AddSingleton<IManagedMethodFunctionTypeResolver,
            ManagedMethodFunctionTypeResolver>();
        services.AddSingleton<IValueFrameLayoutPlanner, ValueFrameLayoutPlanner>();
        services.AddSingleton<FilterEnvironmentLayoutPlanner>();
        services.AddSingleton<IFilterEnvironmentLayoutPlanner>(provider =>
            provider.GetRequiredService<FilterEnvironmentLayoutPlanner>());
        services.AddSingleton<ValueFrameAddressEmitter>();
        services.AddSingleton<IValueFrameAddressEmitter>(provider =>
            provider.GetRequiredService<ValueFrameAddressEmitter>());
        services.AddSingleton<IFilterEnvironmentRootEmitter,
            FilterEnvironmentRootEmitter>();
        services.AddSingleton<IStackTraceFrameEntryEmitter,
            StackTraceFrameEntryEmitter>();
        services.AddSingleton<IStackTraceFrameExitEmitter,
            StackTraceFrameExitEmitter>();
        services.AddSingleton<IMethodFrameEntryEmitter, MethodFrameEntryEmitter>();
        services.AddSingleton<IMethodFrameExitEmitter, MethodFrameExitEmitter>();
        services.AddSingleton<MethodExceptionBoundaryEmitter>();
        services.AddSingleton<IMethodExceptionBoundaryEmitter>(provider =>
            provider.GetRequiredService<MethodExceptionBoundaryEmitter>());
        services.AddSingleton<ManagedMethodEmitter>();
        services.AddSingleton<IInstructionCountingWriterFactory,
            InstructionCountingWriterFactory>();
        services.AddSingleton<IManagedMethodEmitter>(provider =>
            provider.GetRequiredService<ManagedMethodEmitter>());
        services.AddSingleton<ExceptionRegionEmitter>();
        services.AddSingleton<IExceptionRegionEmitter>(provider =>
            provider.GetRequiredService<ExceptionRegionEmitter>());
        services.AddSingleton<IControlFlowDispatcherEmitter,
            ControlFlowDispatcherEmitter>();
        services.AddSingleton<StructuredControlFlowEmitter>();
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
