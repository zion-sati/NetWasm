using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Loading;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Symbols;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler;

public interface INetWasmCompiler
{
    CompilationResult Compile(CompilerOptions options);
}

internal sealed class NetWasmCompilationPipeline(
    ICompilationPipelineExecutor executor) : INetWasmCompiler
{
    private readonly ICompilationPipelineExecutor _executor = executor ??
        throw new ArgumentNullException(nameof(executor));

    public CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _executor.Execute(options).Full ??
            throw new InvalidOperationException("A full compile was served from the last compile.");
    }
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
        services.AddCompilerDiagnostics();
        services.AddCompilerValidation();
        services.AddCompilerSymbols();
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
