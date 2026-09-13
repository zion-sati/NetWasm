using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationComplexityStage
{
    void Write(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        System.Collections.Immutable.ImmutableArray<WasmManagedMethodEmissionMetric> emissions);
}

internal sealed class CompilationComplexityStage(
    ICompilerComplexityMetricsCollector metrics,
    ICompilerComplexityInvariantValidator invariants,
    ICompilerComplexityDiagnosticArtifactWriter artifacts,
    ISymbolFormatterFactory symbols) : ICompilationComplexityStage
{
    private readonly ICompilerComplexityMetricsCollector _metrics = metrics ??
        throw new ArgumentNullException(nameof(metrics));
    private readonly ICompilerComplexityInvariantValidator _invariants = invariants ??
        throw new ArgumentNullException(nameof(invariants));
    private readonly ICompilerComplexityDiagnosticArtifactWriter _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));

    public void Write(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        System.Collections.Immutable.ImmutableArray<WasmManagedMethodEmissionMetric> emissions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(lowering);
        var report = _metrics.Collect(
            _symbols.Create(metadata),
            program,
            lowering,
            emissions);
        _invariants.Validate(report);
        _artifacts.WriteComplexity(options, report);
    }
}
