using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Symbols;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler;

/// <summary>
/// A compile's module and what a host writes beside it, without the analysed program and
/// layouts: what a build needs, which a compile the early cutoff serves from the last one
/// can give too.
/// </summary>
public sealed record ModuleCompilationResult(
    byte[] ApplicationModule,
    HostInteropManifest InteropManifest,
    int StaticDataEnd)
{
    public ImmutableArray<WasmFunctionImport> FunctionImports { get; init; } = [];
    public ImmutableArray<WasmNativeImport> NativeImports { get; init; } = [];
    public ImmutableArray<string> RuntimeFeatures { get; init; } = [];
    public WasmNativeCallbackSupportArtifact? NativeCallbackSupport { get; init; }
    public SymbolSidecarArtifact? TypeSymbols { get; init; }
    public SymbolSidecarArtifact? MethodSymbols { get; init; }

    // The entry point's metadata token in the entry assembly, when the program has one.
    public int? EntryPointToken { get; init; }

    // Whether the entry point runs to completion when called: false for an asynchronous
    // entry the host drives.
    public bool SynchronousEntry { get; init; } = true;

    // Whether the early cutoff served the compile by patching the last compile's module.
    public bool PatchedLastCompile { get; init; }

    public static ModuleCompilationResult Of(CompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var entry = result.Program.EntryPoint;
        return new(result.ApplicationModule, result.InteropManifest, result.StaticDataEnd)
        {
            FunctionImports = result.FunctionImports,
            NativeImports = result.NativeImports,
            RuntimeFeatures = result.RuntimeFeatures,
            NativeCallbackSupport = result.NativeCallbackSupport,
            TypeSymbols = result.TypeSymbols,
            MethodSymbols = result.MethodSymbols,
            EntryPointToken = entry?.Key.MetadataToken,
            // An asynchronous Main is a callback-lifted wasi:cli/run.
            SynchronousEntry = entry is null ||
                !result.Program.JavaScriptAsyncBindings.ContainsKey(entry.Key),
        };
    }
}

/// <summary>
/// Compiles a module for a build. With compiled units on, when only method bodies changed
/// and each edited method adds to the program what it added last time, the last compile's
/// module is patched with the edited methods instead of compiling the program again.
/// </summary>
public interface INetWasmModuleCompiler
{
    ModuleCompilationResult Compile(CompilerOptions options);
}

internal sealed class NetWasmModuleCompilationPipeline(
    ICompilationPipelineExecutor executor,
    Microsoft.Extensions.Logging.ILogger<CompilerDiagnosticCompilationDecorator> logger,
    ICompilerDiagnosticLogPathResolver logPaths) : INetWasmModuleCompiler
{
    public ModuleCompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CompilerDiagnosticCompilationDecorator.Logged(logger, logPaths, options,
            () => executor.Execute(options, serveFromLastCompile: true).Module);
    }
}

/// <summary>A compile's result in full when it ran in full, and its module in any case.</summary>
internal sealed record CompilationOutcome(CompilationResult? Full, ModuleCompilationResult Module);
