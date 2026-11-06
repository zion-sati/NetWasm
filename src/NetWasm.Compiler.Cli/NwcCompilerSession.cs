using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Cli;

internal interface INwcCompilerSession : IDisposable
{
    ManagedModuleCompilation Compile(CompilerOptions options);
}

internal interface INwcCompilerSessionFactory
{
    INwcCompilerSession Create(ExternalToolCommand? wasmTools);
}

internal sealed class NwcCompilerSessionFactory(IConsole console) : INwcCompilerSessionFactory
{
    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));

    public INwcCompilerSession Create(ExternalToolCommand? wasmTools) =>
        NwcCompilerComposition.CreateSession(_console, wasmTools, CachingWasmTools.DefaultDirectory());
}

// The composition root of one compilation: the compiler with the wasm-tools the
// options name, cached where the host keeps caches (if it has a place), and nwc's
// console as a logging provider beside the diagnostic log.
internal static class NwcCompilerComposition
{
    // The wasm-tools a command names, cached in the directory, if there is one.
    internal static IWasmTools Tools(IWasmTools tools, ExternalToolCommand command, string? cacheDirectory) =>
        cacheDirectory is { } cache ? new CachingWasmTools(tools, command, cache) : tools;

    public static NwcCompilerSession CreateSession(IConsole console, ExternalToolCommand? wasmTools, string? cacheDirectory)
    {
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddLogging(logging => logging
            .AddProvider(new NwcConsoleLoggerProvider(console))
            .AddFilter<NwcConsoleLoggerProvider>(NwcConsoleLoggerProvider.Shows));
        if (wasmTools is not null)
        {
            services.RemoveAll<IWasmTools>();
            services.AddSingleton<IWasmTools>(provider => Tools(
                new ProcessWasmTools(provider.GetRequiredService<IExternalToolRunner>(), wasmTools), wasmTools, cacheDirectory));
        }
        var provider = services.BuildServiceProvider();
        return new NwcCompilerSession(provider.GetRequiredService<INetWasmModuleCompiler>(), provider);
    }
}

// One compilation's compiler, and the services it disposes with it.
internal sealed class NwcCompilerSession(INetWasmModuleCompiler compiler, IDisposable services) : INwcCompilerSession
{
    private readonly INetWasmModuleCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly IDisposable _services = services ?? throw new ArgumentNullException(nameof(services));

    public ManagedModuleCompilation Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = _compiler.Compile(options);
        return new(
            result.ApplicationModule,
            result.StaticDataEnd,
            result.InteropManifest,
            result.StackTraceSymbols,
            options.Target == Core.WasmTarget.Wasm64 ? "wasm64" : "wasm32",
            RuntimeFeatures: result.RuntimeFeatures,
            FunctionImports: result.FunctionImports)
        {
            ExceptionTypeMap = result.DiagnosticArtifacts?.ExceptionTypeMap,
            NativeImports = result.NativeImports,
            NativeCallbackSupport = result.NativeCallbackSupport,
            SynchronousEntry = result.SynchronousEntry,
        };
    }

    public void Dispose() => _services.Dispose();
}
