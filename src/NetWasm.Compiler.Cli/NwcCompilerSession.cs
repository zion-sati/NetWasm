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
        NwcCompilerComposition.CreateSession(_console, wasmTools);
}

// The composition root of one compilation: the compiler with the wasm-tools the
// options name, and nwc's console as a logging provider beside the diagnostic log.
internal static class NwcCompilerComposition
{
    public static NwcCompilerSession CreateSession(IConsole console, ExternalToolCommand? wasmTools)
    {
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddLogging(logging => logging
            .AddProvider(new NwcConsoleLoggerProvider(console))
            .AddFilter<NwcConsoleLoggerProvider>(NwcConsoleLoggerProvider.Shows));
        if (wasmTools is not null)
        {
            services.RemoveAll<IWasmTools>();
            services.AddSingleton<IWasmTools>(provider => new ProcessWasmTools(
                provider.GetRequiredService<IExternalToolRunner>(),
                wasmTools));
        }
        var provider = services.BuildServiceProvider();
        return new NwcCompilerSession(provider.GetRequiredService<INetWasmCompiler>(), provider);
    }
}

// One compilation's compiler, and the services it disposes with it.
internal sealed class NwcCompilerSession(INetWasmCompiler compiler, IDisposable services) : INwcCompilerSession
{
    private readonly INetWasmCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
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
            SynchronousEntry = result.Program is not { EntryPoint: { } entry } program ||
                !program.JavaScriptAsyncBindings.TryGetValue(entry.Key, out var binding) ||
                binding.CommandEntryPoint is not null,
        };
    }

    public void Dispose() => _services.Dispose();
}
