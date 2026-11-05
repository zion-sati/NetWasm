using System;
using System.Globalization;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationPipelineExecutor(
    IMetadataCompilationLoader metadataCompilations,
    ICompilationMetadataStage metadataStage,
    ICompilationPreparationBuilder preparation,
    ICompilationAnalysisBuilder analysis,
    ICompilationLayoutBuilder layouts,
    ICompilationRootMapBuilder rootMaps,
    ICompilationEmissionBuilder emission,
    ICompilationDiagnosticTraceStage diagnosticTrace,
    ICompilerInitialDiagnosticArtifactWriter initialArtifacts,
    ICompilerEntryDiagnosticArtifactWriter entryArtifacts,
    ICompilerProgramDiagnosticArtifactWriter programArtifacts,
    ICompilerLayoutDiagnosticArtifactWriter layoutArtifacts,
    ICompilerRootMapDiagnosticArtifactWriter rootMapArtifacts,
    ICompilerEmissionDiagnosticArtifactWriter emissionArtifacts,
    ICompilerSuccessDiagnosticArtifactWriter successArtifacts,
    ICompilerFailureDiagnosticArtifactWriter failureArtifacts,
    IHostInteropManifestBuilder interopManifests,
    ICompilationComplexityStage complexity,
    ICompilerProgressReporter progress,
    ICompilationDiagnosticBindingStage diagnosticBinding,
    ICompilationStackTraceArtifactBinder stackTraceArtifacts,
    ISymbolFormatterFactory symbols,
    ICompilationMetricsRequestFactory metricsRequests,
    ICompilationInputDetector inputDetector,
    ICompilationInputRecorder inputRecorder,
    ICompiledUnitStateFactory unitStates,
    IEarlyCutoffEmitter earlyCutoff,
    IUnitKeptCompileCommitter keptCompiles) :
    ICompilationPipelineExecutor
{
    private readonly IUnitKeptCompileCommitter _keptCompiles = keptCompiles ??
        throw new ArgumentNullException(nameof(keptCompiles));
    private readonly IEarlyCutoffEmitter _earlyCutoff = earlyCutoff ??
        throw new ArgumentNullException(nameof(earlyCutoff));
    private readonly IMetadataCompilationLoader _metadataCompilations = metadataCompilations ??
        throw new ArgumentNullException(nameof(metadataCompilations));
    private readonly ICompilationMetadataStage _metadataStage = metadataStage ??
        throw new ArgumentNullException(nameof(metadataStage));
    private readonly ICompilationPreparationBuilder _preparation = preparation ??
        throw new ArgumentNullException(nameof(preparation));
    private readonly ICompilationAnalysisBuilder _analysis = analysis ??
        throw new ArgumentNullException(nameof(analysis));
    private readonly ICompilationLayoutBuilder _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly ICompilationRootMapBuilder _rootMaps = rootMaps ??
        throw new ArgumentNullException(nameof(rootMaps));
    private readonly ICompilationEmissionBuilder _emission = emission ??
        throw new ArgumentNullException(nameof(emission));
    private readonly ICompilationDiagnosticTraceStage _diagnosticTrace = diagnosticTrace ??
        throw new ArgumentNullException(nameof(diagnosticTrace));
    private readonly ICompilerInitialDiagnosticArtifactWriter _initialArtifacts = initialArtifacts ??
        throw new ArgumentNullException(nameof(initialArtifacts));
    private readonly ICompilerEntryDiagnosticArtifactWriter _entryArtifacts = entryArtifacts ??
        throw new ArgumentNullException(nameof(entryArtifacts));
    private readonly ICompilerProgramDiagnosticArtifactWriter _programArtifacts = programArtifacts ??
        throw new ArgumentNullException(nameof(programArtifacts));
    private readonly ICompilerLayoutDiagnosticArtifactWriter _layoutArtifacts = layoutArtifacts ??
        throw new ArgumentNullException(nameof(layoutArtifacts));
    private readonly ICompilerRootMapDiagnosticArtifactWriter _rootMapArtifacts = rootMapArtifacts ??
        throw new ArgumentNullException(nameof(rootMapArtifacts));
    private readonly ICompilerEmissionDiagnosticArtifactWriter _emissionArtifacts = emissionArtifacts ??
        throw new ArgumentNullException(nameof(emissionArtifacts));
    private readonly ICompilerSuccessDiagnosticArtifactWriter _successArtifacts = successArtifacts ??
        throw new ArgumentNullException(nameof(successArtifacts));
    private readonly ICompilerFailureDiagnosticArtifactWriter _failureArtifacts = failureArtifacts ??
        throw new ArgumentNullException(nameof(failureArtifacts));
    private readonly IHostInteropManifestBuilder _interopManifests = interopManifests ??
        throw new ArgumentNullException(nameof(interopManifests));
    private readonly ICompilationComplexityStage _complexity = complexity ??
        throw new ArgumentNullException(nameof(complexity));
    private readonly ICompilerProgressReporter _progress = progress ??
        throw new ArgumentNullException(nameof(progress));
    private readonly ICompilationDiagnosticBindingStage _diagnosticBinding = diagnosticBinding ??
        throw new ArgumentNullException(nameof(diagnosticBinding));
    private readonly ICompilationStackTraceArtifactBinder _stackTraceArtifacts =
        stackTraceArtifacts ?? throw new ArgumentNullException(nameof(stackTraceArtifacts));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly ICompilationMetricsRequestFactory _metricsRequests = metricsRequests ??
        throw new ArgumentNullException(nameof(metricsRequests));

    private readonly ICompiledUnitStateFactory _unitStates = unitStates ??
        throw new ArgumentNullException(nameof(unitStates));
    private readonly ICompilationInputDetector _inputDetector = inputDetector ??
        throw new ArgumentNullException(nameof(inputDetector));
    private readonly ICompilationInputRecorder _inputRecorder = inputRecorder ??
        throw new ArgumentNullException(nameof(inputRecorder));

    // The module the early cutoff patched, with the facts the last compile kept, once the
    // patched compile is kept in its place; null when the cutoff declined.
    private ModuleCompilationResult? Serve(CompilerOptions options, MetadataCompilationSnapshot metadata,
        CompilationInputObservation inputs, EarlyCutoffEmission served)
    {
        if (served is not { Module: { } patched, Kept: { } kept, Facts: { } facts }) return null;
        DiagnosticArtifactBundle? diagnostics = null;
        if (options.EmitExceptionTypeMap)
        {
            if (facts.ExceptionTypes is not { } exceptionTypes) return null;
            diagnostics = _diagnosticBinding.Bind(patched, exceptionTypes, metadata, options);
        }
        _keptCompiles.Commit(inputs.Directory, kept with { Module = patched });
        _inputRecorder.Record(inputs with { Report = inputs.Report + served.Account });
        _emissionArtifacts.WriteEmission(options, patched);
        return new ModuleCompilationResult(patched, facts.InteropManifest, facts.StaticDataEnd)
        {
            FunctionImports = facts.FunctionImports,
            RuntimeFeatures = facts.RuntimeFeatures,
            EntryPointToken = facts.EntryPointToken,
            SynchronousEntry = facts.SynchronousEntry,
            DiagnosticArtifacts = diagnostics,
            StackTraceSymbols = served.StackTraceSymbols,
            PatchedLastCompile = true,
        };
    }

    public CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        var metrics = options.MetricsObserver is null
            ? null
            : _metricsRequests.Create(options.MetricsObserver);
        var stage = "initialization";
        metrics?.Stages.Start(CompilerMetricStage.Initialization);
        try
        {
            _progress.Report(CompilerProgressStage.Start);
            _initialArtifacts.WriteInitialArtifacts(options);
            stage = "metadata";
            metrics?.Stages.Start(CompilerMetricStage.Metadata);
            CompilationOutcome compiledResult;
            using (var lease = options.ReferenceAssemblyAliases is null or { Count: 0 }
                ? _metadataCompilations.Load(options.EntryAssemblyPath, options.ReferencePaths)
                : _metadataCompilations.Load(
                    options.EntryAssemblyPath,
                    options.ReferencePaths,
                    options.ReferenceAssemblyAliases))
            {
                var metadata = lease.Snapshot;
                _metadataStage.ValidateAndWrite(options, metadata);
                var inputs = _inputDetector.Detect(options, metadata);
                var symbolFormatter = _symbols.Create(metadata);
                metrics?.Counters.Count("referenceAssemblies", options.ReferencePaths.Length);
                _progress.Report(CompilerProgressStage.Metadata);

                stage = "entry";
                metrics?.Stages.Start(CompilerMetricStage.Preparation);
                var prepared = _preparation.Prepare(metadata, options);
                _entryArtifacts.WriteEntry(
                    options,
                    symbolFormatter,
                    prepared.Entry);
                metrics?.Counters.Count("exports", prepared.Exports.Length);
                _progress.Report(CompilerProgressStage.Entry);

                // When only bodies changed and the edited methods add what they did, the last
                // compile's module is patched with them instead (not under verification, which
                // proves the cutoff against a full compile below).
                EarlyCutoffEmission? declined = null;
                if (serveFromLastCompile && inputs is not null && !options.VerifyCompiledUnits)
                {
                    stage = "early cutoff";
                    var served = _earlyCutoff.Emit(new(options, metadata, prepared, inputs));
                    if (Serve(options, metadata, inputs, served) is { } module)
                    {
                        metrics?.Counters.Count("moduleBytes", module.ApplicationModule.Length);
                        _successArtifacts.WriteSuccess(options);
                        _progress.Report(CompilerProgressStage.Complete);
                        metrics?.Reports.Complete(CompilerMetricsOutcome.Succeeded, null);
                        return new CompilationOutcome(null, module);
                    }
                    declined = served;
                }

                stage = "analysis";
                metrics?.Stages.Start(CompilerMetricStage.Analysis);
                var analyzed = _analysis.Analyze(metadata, prepared);
                _programArtifacts.WriteProgram(
                    options,
                    symbolFormatter,
                    analyzed.Program);
                metrics?.Counters.Count("methods", analyzed.Program.Methods.Count);
                metrics?.Counters.Count("constructedMethods", analyzed.Program.ConstructedMethods.Count);
                metrics?.Counters.Count("types", analyzed.Program.Types.Count);
                metrics?.Counters.Count("fields", analyzed.Program.Fields.Count);
                _progress.Report(CompilerProgressStage.Analysis);

                stage = "layouts";
                metrics?.Stages.Start(CompilerMetricStage.Layouts);
                var compiledLayouts = _layouts.Compile(
                    metadata,
                    analyzed.Program,
                    options.Target);
                _layoutArtifacts.WriteLayouts(options, compiledLayouts.Snapshot);
                metrics?.Counters.Count("objectLayouts", compiledLayouts.Snapshot.ObjectLayouts.Count);
                metrics?.Counters.Count("valueLayouts", compiledLayouts.Snapshot.ValueLayouts.Count);
                metrics?.Counters.Count("dataSegments", compiledLayouts.Snapshot.DataSegments.Length);
                _progress.Report(CompilerProgressStage.Layouts);

                stage = "root maps";
                metrics?.Stages.Start(CompilerMetricStage.RootMaps);
                // The last compile's units serve root maps and emission.
                using var units = _unitStates.Create(options, metadata, inputs, analyzed.Program,
                    prepared.OptimizationSettings.BranchPrefixDuplication,
                    prepared.OptimizationSettings.DispatcherElimination);
                // Verification proves the early cutoff too: where it applies, it patches the
                // last compile's module, which this compile's must equal. It reads the last
                // compile, so it runs before emission keeps this one.
                var cutoff = options.VerifyCompiledUnits && inputs is not null
                    ? _earlyCutoff.Emit(new(options, metadata, prepared, inputs))
                    : declined;
                var mappedProgram = _rootMaps.Analyze(
                    metadata,
                    compiledLayouts,
                    analyzed.Program,
                    units);
                _rootMapArtifacts.WriteRootMaps(options, mappedProgram);
                metrics?.Counters.Count("methodRootMaps", mappedProgram.RootMaps.Count);
                metrics?.Counters.Count("constructedRootMaps", mappedProgram.ConstructedRootMaps.Count);
                _progress.Report(CompilerProgressStage.RootMaps);

                stage = "emission";
                metrics?.Stages.Start(CompilerMetricStage.LoweringAndEmission);
                var emitted = _emission.Emit(
                    metadata,
                    prepared,
                    new CompilationAnalysis(mappedProgram),
                    compiledLayouts,
                    options,
                    units);
                metrics?.Counters.Count("moduleBytes", emitted.Result.Module.Length);
                if (options.VerifyCompiledUnits && cutoff?.Module is { } patched &&
                    !patched.AsSpan().SequenceEqual(emitted.Result.Module))
                {
                    throw new InvalidOperationException(
                        "The early cutoff patched a module that differs from the full compile's at byte " +
                        patched.AsSpan().CommonPrefixLength(emitted.Result.Module).ToString(CultureInfo.InvariantCulture) +
                        $" ({cutoff.Account.TrimEnd()}).");
                }
                _progress.Report(CompilerProgressStage.Emission);

                stage = "complexity";
                metrics?.Stages.Start(CompilerMetricStage.Complexity);
                _complexity.Write(
                    options,
                    metadata,
                    mappedProgram,
                    emitted.Lowering,
                    emitted.Result.ManagedMethodMetrics);
                _progress.Report(CompilerProgressStage.Complexity);

                stage = "validation";
                metrics?.Stages.Start(CompilerMetricStage.ValidationAndDiagnostics);
                _emissionArtifacts.WriteEmission(options, emitted.Result.Module);
                _progress.Report(CompilerProgressStage.Validation);
                if (options.DiagnosticTracePath is not null)
                {
                    _diagnosticTrace.Write(
                        options.DiagnosticTracePath,
                        metadata,
                        mappedProgram,
                        emitted.Lowering,
                        compiledLayouts,
                        prepared.Entry.EntryPoint,
                        options.Target,
                        emitted.Result.StaticDataEnd);
                }

                stage = "interop manifest";
                metrics?.Stages.Start(CompilerMetricStage.InteropManifest);
                var interopManifest = _interopManifests.Build(
                    mappedProgram,
                    prepared.Exports,
                    compiledLayouts.Snapshot,
                    [.. emitted.Result.ImportModules, .. options.ImportModules]);
                _progress.Report(CompilerProgressStage.InteropManifest);
                _successArtifacts.WriteSuccess(options);
                _progress.Report(CompilerProgressStage.Complete);

                stage = "result projection and binding";
                metrics?.Stages.Start(CompilerMetricStage.ResultProjectionAndBinding);
                var result = _stackTraceArtifacts.Bind(
                    new CompilationResult(
                        emitted.Result.Module,
                        mappedProgram,
                        compiledLayouts.Snapshot,
                        interopManifest,
                        emitted.Result.StaticDataEnd)
                    {
                        FunctionImports = emitted.Result.FunctionImports,
                        NativeImports = emitted.Result.NativeImports,
                        RuntimeFeatures = emitted.Result.RuntimeFeatures,
                        NativeCallbackSupport = emitted.Result.NativeCallbackSupport,
                    },
                    emitted.Result.StackTraceSymbols,
                    options);
                result = _diagnosticBinding.Bind(
                    result,
                    metadata,
                    options);
                metrics?.Counters.Count("functionImports", result.FunctionImports.Length);
                metrics?.Counters.Count("runtimeFeatures", result.RuntimeFeatures.Length);
                _inputRecorder.Record(cutoff is null || inputs is null ? inputs : inputs with
                {
                    Report = inputs.Report + cutoff.Account,
                }, mappedProgram);
                var moduleResult = ModuleCompilationResult.Of(result);
                // The compile is kept for the next to patch once its facts are known.
                if (units is { Directory: { } directory, Kept: { } kept })
                {
                    _keptCompiles.Commit(directory, kept with
                    {
                        Facts = KeptCompilationFacts.Write(KeptCompilationFacts.Of(options, moduleResult)),
                    });
                }
                compiledResult = new CompilationOutcome(result, moduleResult);
            }
            metrics?.Reports.Complete(CompilerMetricsOutcome.Succeeded, null);
            return compiledResult;
        }
        catch (Exception exception)
        {
            metrics?.Stages.Start(CompilerMetricStage.FailureReporting);
            try
            {
                _failureArtifacts.WriteFailure(options, stage, exception);
            }
            finally
            {
                metrics?.Reports.Complete(
                    exception is OperationCanceledException
                        ? CompilerMetricsOutcome.Canceled
                        : CompilerMetricsOutcome.Failed,
                    stage);
            }
            throw;
        }
    }
}
