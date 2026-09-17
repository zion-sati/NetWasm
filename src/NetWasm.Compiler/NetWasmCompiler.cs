using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Loading;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler;

public static class NetWasmCompiler
{
    public static CompilationResult Compile(CompilerOptions options)
    {
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }
}

public interface INetWasmCompiler
{
    CompilationResult Compile(CompilerOptions options);
}

internal sealed class NetWasmCompilationPipeline(
    ICompilationPipelineExecutor executor,
    IFrontendArtifactCacheRequestFactory frontendArtifacts,
    IFrontendCacheMetricsObserverFactory metricsObservers) : INetWasmCompiler
{
    private readonly ICompilationPipelineExecutor _executor = executor ??
        throw new ArgumentNullException(nameof(executor));
    private readonly IFrontendArtifactCacheRequestFactory _frontendArtifacts =
        frontendArtifacts ?? throw new ArgumentNullException(nameof(frontendArtifacts));
    private readonly IFrontendCacheMetricsObserverFactory _metricsObservers =
        metricsObservers ?? throw new ArgumentNullException(nameof(metricsObservers));

    public CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var frontendRequest = _frontendArtifacts.Begin(options);
        var observer = options.MetricsObserver;
        var result = _executor.Execute(observer is null ? options : options with
        {
            MetricsObserver = _metricsObservers.Create(observer)
        });
        frontendRequest.Commit();
        return result;
    }
}

internal interface IFrontendCacheMetricsObserverFactory
{
    ICompilerMetricsObserver Create(ICompilerMetricsObserver observer);
}

internal sealed class FrontendCacheMetricsObserverFactory(
    IFrontendArtifactCacheMetricsReader metrics) : IFrontendCacheMetricsObserverFactory
{
    private readonly IFrontendArtifactCacheMetricsReader _metrics = metrics ??
        throw new ArgumentNullException(nameof(metrics));

    public ICompilerMetricsObserver Create(ICompilerMetricsObserver observer) =>
        new FrontendCacheMetricsObserver(
            observer ?? throw new ArgumentNullException(nameof(observer)),
            _metrics);
}

internal sealed class FrontendCacheMetricsObserver(
    ICompilerMetricsObserver inner,
    IFrontendArtifactCacheMetricsReader metrics) : ICompilerMetricsObserver
{
    public void Report(CompilerMetricsReport report) =>
        inner.Report(report with { FrontendCache = metrics.Read() });
}

public static class NetWasmCompilerServiceCollectionExtensions
{
    public static IServiceCollection AddNetWasmCompiler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCompilerLoading();
        services.AddCompilerControlFlow();
        services.AddCompilerAnalysis();
        services.AddCompilerLayout();
        services.AddCompilerGarbageCollection();
        services.AddCompilerEmission();
        services.AddCompilerFrontendCaching();
        services.AddCompilerDiagnostics();
        services.AddCompilerValidation();
        services.AddCompilerExceptionTypes();
        services.AddCompilerStackTraces();
        services.AddCompilerInterop();
        services.AddCompilerEntryPoints();
        services.AddCompilerComponentModel();
        services.AddCompilerPipeline();
        services.AddSingleton<INetWasmCompiler>(static services =>
            new CompilerDiagnosticCompilationDecorator(
                services.GetRequiredService<NetWasmCompilationPipeline>(),
                services.GetRequiredService<ILogger<CompilerDiagnosticCompilationDecorator>>(),
                services.GetRequiredService<ICompilerDiagnosticLogPathResolver>()));
        return services;
    }
}
