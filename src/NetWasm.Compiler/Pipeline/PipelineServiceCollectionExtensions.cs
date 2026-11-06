using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(CompilerParallelism.ForHost(
            OperatingSystem.IsBrowser(), Environment.ProcessorCount));
        services.AddSingleton<IIndexedWorkExecutor, IndexedWorkExecutor>();
        services.AddSingleton<IComponentExportMerger, ComponentExportMerger>();
        services.AddSingleton<IComponentReachabilityValidator,
            ComponentReachabilityValidator>();
        services.AddSingleton<ICompilationPreparationBuilder,
            CompilationPreparationBuilder>();
        services.AddSingleton<ICompilationAnalysisBuilder,
            CompilationAnalysisBuilder>();
        services.AddSingleton<ICompilationLayoutBuilder,
            CompilationLayoutBuilder>();
        services.AddSingleton<ICompilationRootMapBuilder,
            CompilationRootMapBuilder>();
        services.AddSingleton<ICompilationEmissionBuilder,
            CompilationEmissionBuilder>();
        services.AddSingleton<ICompiledUnitStateFactory, CompiledUnitStateFactory>();
        // A compile that replays the last one resolves function indices from its kept plans.
        services.AddSingleton<NetWasm.Compiler.Wasm.Emission.Planning.IFunctionIndexResolverFactory,
            NetWasm.Compiler.Wasm.Emission.Planning.FunctionIndexResolverFactory>();
        // A last compile that cannot answer declines the cutoff.
        services.AddSingleton<EarlyCutoffEmitter>();
        services.AddSingleton<IEarlyCutoffEmitter>(provider =>
            new DecliningEarlyCutoffEmitter(provider.GetRequiredService<EarlyCutoffEmitter>()));
        services.AddSingleton<INetWasmModuleCompiler, NetWasmModuleCompilationPipeline>();
        services.AddSingleton<IManagedByRefMethodSummarizerFactory, ManagedByRefMethodSummarizerFactory>();
        services.AddSingleton<NetWasm.Compiler.Wasm.Units.IReplayMethodEmitterFactory,
            NetWasm.Compiler.Wasm.Units.ReplayMethodEmitterFactory>();
        services.TryAddSingleton<NetWasm.Compiler.Wasm.Emission.Methods.IExceptionGroupEnumerator,
            NetWasm.Compiler.Wasm.Emission.Methods.ExceptionGroupEnumerator>();
        services.AddSingleton<IEditedMethodAnalyzer, EditedMethodAnalyzer>();
        services.AddSingleton<NetWasm.Compiler.Wasm.Units.IMethodOutlineProviderFactory,
            NetWasm.Compiler.Wasm.Units.CompiledUnitOutlinesFactory>();
        services.AddSingleton<ICompilationMetadataStage, CompilationMetadataStage>();
        services.AddSingleton<ICompilationDiagnosticTraceStage,
            CompilationDiagnosticTraceStage>();
        services.AddSingleton<ICompilationComplexityStage, CompilationComplexityStage>();
        services.AddSingleton<ICompilationDiagnosticBindingStage,
            CompilationDiagnosticBindingStage>();
        services.AddSingleton<ICompilationTimestampReader, CompilationTimestampReader>();
        services.AddSingleton<ICompilationElapsedTimeCalculator,
            CompilationElapsedTimeCalculator>();
        services.AddSingleton<ICompilationMetricsRequestFactory,
            CompilationMetricsRequestFactory>();
        services.AddSingleton<NetWasm.Compiler.Metadata.Inputs.ICompilationInputReader,
            NetWasm.Compiler.Metadata.Inputs.CompilationInputReader>();
        services.AddSingleton<ICompilationInputDetector, CompilationInputDetector>();
        services.AddSingleton<ICompilationInputRecorder, CompilationInputRecorder>();
        services.AddSingleton<ICompilationPipelineExecutor,
            CompilationPipelineExecutor>();
        services.AddSingleton<NetWasmCompilationPipeline>();
        return services;
    }
}
