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
    // What each target compiles its entry point as.
    internal static readonly FrozenDictionary<NwcTarget, CompilerEntryPointKind> EntryPointKinds =
        new System.Collections.Generic.Dictionary<NwcTarget, CompilerEntryPointKind>
        {
            [NwcTarget.Exe] = CompilerEntryPointKind.ManagedExecutable,
            [NwcTarget.Library] = CompilerEntryPointKind.Library,
            [NwcTarget.Function] = CompilerEntryPointKind.RawFunction,
            [NwcTarget.CoreModule] = CompilerEntryPointKind.Library,
        }.ToFrozenDictionary();

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
            options.ManagedArrayStoreCheckElimination,
            options.ManagedDispatcherElimination,
            options.ManagedEmptyFinallyRemoval,
            options.ManagedStructScalarReplacement,
            options.ManagedAllocationScalarReplacement,
            options.ManagedIdempotentLoopCollapse,
            options.ManagedLoopStoreForwarding,
            options.ManagedArrayLoopIdioms,
            options.ManagedLoopVectorization,
            options.ManagedPureCallReuse,
            options.ManagedFieldLoadReuse,
            options.ManagedConditionalStoreMerging,
            options.ManagedInlineArrayData,
            options.ManagedQuotientReuse,
            options.ManagedIntegerCheckElimination,
            options.ManagedLoopInvariantExpressionHoisting,
            options.ManagedMulticastInlineDispatch,
            options.ManagedNewObjectNullCheckElimination,
            options.ManagedLoopStrengthReduction,
            options.ManagedRangeBranchFolding,
            options.ManagedUnwrittenStaticFieldFolding);
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
            EmitTypeNames: options.TypeNames,
            EmitStackTrace: options.MethodSymbols is not null,
            EntryMethodToken: entryPoint?.MetadataToken,
            EntryPointKind: EntryPointKinds[options.Target],
            IntermediateOutputPath: options.IntermediateDirectory,
            UseJavaScriptExportBoundary: options.UseJavaScriptExportBoundary,
            ProjectDirectory: options.ProjectDirectory,
            PathMap: options.PathMap,
            StructuredDiagnostics: options.StructuredDiagnostics,
            Optimization: options.Optimization,
            OptimizationOverrides: overrides,
            MetadataReferencePaths: options.MetadataReferences)
        {
            ImportModules = options.ImportModules,
            LinkedNativeLibraries = options.LinkedNativeOnly ? options.NativeLibraries : null,
            FeatureSwitches = options.FeatureSwitches,
            RuntimeProperties = options.RuntimeProperties,
            OriginalContractReferencePaths = options.OriginalContracts,
            ReferenceOrigins = options.ReferenceOrigins,
            PlainCoreModule = options.Target == NwcTarget.CoreModule,
            CompiledUnits = CompiledUnits(Environment.GetEnvironmentVariable("NETWASM_COMPILED_UNITS"), options),
            VerifyCompiledUnits = VerifiesCompiledUnits(Environment.GetEnvironmentVariable("NETWASM_COMPILED_UNITS"), options),
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
            TypeSymbolsPath = options.TypeSymbols,
            MethodSymbolsPath = options.MethodSymbols,
        });
        return 0;
    }

    // Compiled units serve O0 compiles that have an intermediate directory. The environment
    // overrides the switches for a process: 0 turns units off, 1 on, and verify on with every
    // restored body emitted again and compared.
    // Whether compiled units are verified: the environment asks for it, or the options do.
    internal static bool VerifiesCompiledUnits(string? environment, CompileCliOptions options) =>
        environment == "verify" || options.VerifyCompiledUnits;

    internal static bool CompiledUnits(string? environment, CompileCliOptions options) => environment switch
    {
        "0" => false,
        "1" or "verify" => true,
        // Verifying units asks for them; otherwise they are on where builds compile for speed.
        _ => options.CompiledUnits ?? (options.VerifyCompiledUnits ||
            options.Optimization is NetWasm.Compiler.ComponentModel.FinalWasmOptimization.O0 or
                NetWasm.Compiler.ComponentModel.FinalWasmOptimization.None),
    };
}
