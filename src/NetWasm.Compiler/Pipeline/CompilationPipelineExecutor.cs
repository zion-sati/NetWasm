using System;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm.Emission;

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
    ISymbolFormatterFactory symbols) : ICompilationPipelineExecutor
{
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

    public CompilationResult Execute(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stage = "initialization";
        try
        {
            _progress.Report(CompilerProgressStage.Start);
            _initialArtifacts.WriteInitialArtifacts(options);
            stage = "metadata";
            using var lease = options.ReferenceAssemblyAliases is null or { Count: 0 }
                ? _metadataCompilations.Load(options.EntryAssemblyPath, options.ReferencePaths)
                : _metadataCompilations.Load(
                    options.EntryAssemblyPath,
                    options.ReferencePaths,
                    options.ReferenceAssemblyAliases);
            var metadata = lease.Snapshot;
            _metadataStage.ValidateAndWrite(options, metadata);
            var symbolFormatter = _symbols.Create(metadata);
            _progress.Report(CompilerProgressStage.Metadata);

            stage = "entry";
            var prepared = _preparation.Prepare(metadata, options);
            _entryArtifacts.WriteEntry(
                options,
                symbolFormatter,
                prepared.Entry);
            _progress.Report(CompilerProgressStage.Entry);

            stage = "analysis";
            var analyzed = _analysis.Analyze(metadata, prepared);
            _programArtifacts.WriteProgram(
                options,
                symbolFormatter,
                analyzed.Program);
            _progress.Report(CompilerProgressStage.Analysis);

            stage = "layouts";
            var compiledLayouts = _layouts.Compile(
                metadata,
                analyzed.Program,
                options.Target);
            _layoutArtifacts.WriteLayouts(options, compiledLayouts.Snapshot);
            _progress.Report(CompilerProgressStage.Layouts);

            stage = "root maps";
            var mappedProgram = _rootMaps.Analyze(
                metadata,
                compiledLayouts,
                analyzed.Program);
            _rootMapArtifacts.WriteRootMaps(options, mappedProgram);
            _progress.Report(CompilerProgressStage.RootMaps);

            stage = "emission";
            var emitted = _emission.Emit(
                metadata,
                prepared,
                new CompilationAnalysis(mappedProgram),
                compiledLayouts,
                options);
            _progress.Report(CompilerProgressStage.Emission);

            stage = "complexity";
            _complexity.Write(
                options,
                metadata,
                mappedProgram,
                emitted.Lowering,
                emitted.Result.ManagedMethodMetrics);
            _progress.Report(CompilerProgressStage.Complexity);

            stage = "validation";
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
            var interopManifest = _interopManifests.Build(
                mappedProgram,
                prepared.Exports,
                compiledLayouts.Snapshot);
            _progress.Report(CompilerProgressStage.InteropManifest);
            _successArtifacts.WriteSuccess(options);
            _progress.Report(CompilerProgressStage.Complete);
            var result = _stackTraceArtifacts.Bind(
                new CompilationResult(
                    emitted.Result.Module,
                    mappedProgram,
                    compiledLayouts.Snapshot,
                    interopManifest,
                    emitted.Result.StaticDataEnd)
                {
                    FunctionImports = emitted.Result.FunctionImports,
                    RuntimeFeatures = emitted.Result.RuntimeFeatures,
                },
                emitted.Result.StackTraceSymbols,
                options);
            return _diagnosticBinding.Bind(
                result,
                metadata,
                options);
        }
        catch (Exception exception)
        {
            _failureArtifacts.WriteFailure(options, stage, exception);
            throw;
        }
    }
}
