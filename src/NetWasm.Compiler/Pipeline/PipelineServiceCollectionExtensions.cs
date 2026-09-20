using System;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<ICompilationPipelineExecutor,
            CompilationPipelineExecutor>();
        services.AddSingleton<NetWasmCompilationPipeline>();
        return services;
    }
}
