using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Cli;

// nwc's default command: compiles one managed assembly to a core WebAssembly
// module and writes the sidecars the options name.
internal sealed class CompileCliCommand(
    INwcCompilerSessionFactory sessions,
    IManagedEntryPointReader entryPoints,
    ICompilerArtifactWriter artifacts,
    IManagedCompilerOptimizationOverridesParser optimizationOverrides) : ICompilerCliCommand
{
    private readonly INwcCompilerSessionFactory _sessions = sessions ??
        throw new ArgumentNullException(nameof(sessions));
    private readonly IManagedEntryPointReader _entryPoints = entryPoints ??
        throw new ArgumentNullException(nameof(entryPoints));
    private readonly ICompilerArtifactWriter _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly IManagedCompilerOptimizationOverridesParser _optimizationOverrides = optimizationOverrides ??
        throw new ArgumentNullException(nameof(optimizationOverrides));

    public string Name => string.Empty;

    public FrozenSet<string> Options => CompileCliOptions.Names;

    public int Run(NwcArguments arguments)
    {
        var options = CompileCliOptions.Parse(arguments);
        var overrides = _optimizationOverrides.Parse(
            options.ManagedLoopInvariantFieldHoisting,
            options.ManagedStaticDataViewElimination,
            options.ManagedAddressTakenValuePromotion,
            options.ManagedMethodInlining,
            options.ManagedBranchPrefixDuplication,
            options.ManagedScalarCallPreservation,
            options.ManagedArrayBoundsCheckElimination,
            options.ManagedLoopUnrolling,
            options.ManagedConstantArgumentPropagation,
            options.ManagedStaticArrayPopulationElimination,
            options.ManagedConstantCallFolding,
            options.ManagedSwitchBitTests,
            options.ManagedDevirtualization,
            options.ManagedAllocationElimination,
            options.ManagedLoopRotation,
            options.ManagedArrayStoreCheckElimination);
        var entryPoint = options.Target == NwcTarget.Exe ? _entryPoints.Read(options.Input) : null;
        using var session = _sessions.Create(options.WasmTools);
        var compilation = session.Compile(new CompilerOptions(
            options.Input,
            options.References,
            entryPoint?.TypeName ?? options.EntryType,
            entryPoint?.MethodName ?? options.EntryMethod,
            options.Exports,
            options.Platform,
            options.DiagnosticTrace,
            options.DiagnosticLog,
            options.Wit,
            options.World,
            options.Sources,
            EmitStackTrace: options.StackTraceSymbols is not null,
            EntryMethodToken: entryPoint?.MetadataToken,
            EntryPointKind: options.Target switch
            {
                NwcTarget.Exe => CompilerEntryPointKind.ManagedExecutable,
                NwcTarget.Library => CompilerEntryPointKind.Library,
                _ => CompilerEntryPointKind.RawFunction,
            },
            EnableFrontendCache: options.IntermediateDirectory is not null,
            IntermediateOutputPath: options.IntermediateDirectory,
            UseJavaScriptExportBoundary: options.UseJavaScriptExportBoundary,
            ProjectDirectory: options.ProjectDirectory,
            PathMap: options.PathMap,
            StructuredDiagnostics: options.StructuredDiagnostics,
            Optimization: options.Optimization,
            OptimizationOverrides: overrides)
        {
            ImportModules = options.ImportModules,
        });
        // The recorded entry ABI describes the export the compiler produced, which hosts follow.
        compilation = compilation with
        {
            EntryPoint = entryPoint?.Abi is { CompletionShape: ManagedExecutableCompletionShape.Asynchronous } abi &&
                compilation.SynchronousEntry
                ? abi with { CompletionShape = ManagedExecutableCompletionShape.Synchronous }
                : entryPoint?.Abi,
        };
        _artifacts.Write(new CompilerArtifactWriteRequest(
            compilation,
            options.Output,
            options.CallbackObject ?? Path.ChangeExtension(options.Output, ".callbacks.o"))
        {
            RuntimeLayoutPath = options.RuntimeLayout,
            InteropManifestPath = options.InteropManifest,
            CompilerMetadataPath = options.CompilerMetadata,
            ExceptionTypeMapPath = options.ExceptionTypeMap,
            StackTraceSymbolsPath = options.StackTraceSymbols,
        });
        return 0;
    }
}
