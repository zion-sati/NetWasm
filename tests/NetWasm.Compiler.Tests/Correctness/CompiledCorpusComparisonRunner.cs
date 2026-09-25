using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CompiledCorpusComparisonRunner(
    INetWasmOracleRunner netWasm,
    ILinkedCorpusOracleRunner linked,
    ILinkedCorpusReceiptDestinationValidator receiptDestinations,
    ILinkedCorpusQualificationReceiptWriter receipts,
    IOracleComparer comparer,
    ICompilerFailureArtifactWriter failures,
    IRandomCilCaseProgressReporter progress,
    ICorpusExpectationVerifier expectations,
    ICorpusExecutionVerifier executions,
    ICorpusCompilationCellsSelector selections) : ICompiledCorpusComparisonRunner
{
    public void RunAgainstOracle(
        CorpusCompilation compilation,
        ImmutableDictionary<int, OracleObservation> expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(expected);
        var cells = selections.Select(compilation);
        if (!cells.IsDefault)
        {
            compilation = compilation with
            {
                Fixture = compilation.Fixture with
                {
                    ExecuteWasm64 = true,
                    ExecuteOptimizedWasm = cells.Any(cell =>
                        cell.Backend == CorpusExecutionBackend.Simulated &&
                        cell.Form == CorpusWasmForm.Optimized),
                },
            };
        }
        expectations.Verify(compilation.Fixture, expected);
        if (!cells.IsDefault && cells.Any(cell => cell.Backend == CorpusExecutionBackend.Linked))
            receiptDestinations.Validate(compilation.Directory);
        progress.Report(compilation.Fixture.Name, RandomCilCaseStage.NetWasmOracles);
        var simulatedCells = cells.IsDefault
            ? default
            : cells.Where(cell => cell.Backend == CorpusExecutionBackend.Simulated)
                .ToImmutableArray();
        var targets = cells.IsDefault
            ? [NetWasm.Compiler.Core.WasmTarget.Wasm32, NetWasm.Compiler.Core.WasmTarget.Wasm64]
            : simulatedCells.Select(cell => cell.Target).Distinct().ToImmutableArray();
        var results = new List<NetWasmExecution>();
        foreach (var target in targets)
        {
            var execution = netWasm.CompileAndRun(compilation, target, cancellationToken);
            executions.Verify(compilation.Fixture, target, execution);
            results.Add(execution);
        }
        var linkedResults = cells.IsDefault
            ? []
            : cells.Where(cell => cell.Backend == CorpusExecutionBackend.Linked)
                .Select(cell =>
                {
                    var execution = linked.CompileBuildAndRun(compilation, cell, cancellationToken);
                    receipts.Write(compilation, execution, expected);
                    return execution;
                })
                .ToImmutableArray();
        var mismatches = new List<Exception>();
        var compared = 0;
        foreach (var input in compilation.Fixture.Inputs)
        {
            foreach (var execution in results.Where(result => result.Executed))
            {
                if (cells.IsDefault || simulatedCells.Any(cell =>
                    cell.Target == execution.Target && cell.Form == CorpusWasmForm.Direct))
                {
                    VerifyObservation(input, expected[input], execution.Observations[input],
                        execution with
                        {
                            Backend = CorpusExecutionBackend.Simulated,
                            Form = CorpusWasmForm.Direct
                        }, string.Empty);
                }
                if (execution.OptimizedObservations.TryGetValue(input, out var optimized) &&
                    (cells.IsDefault || simulatedCells.Any(cell =>
                        cell.Target == execution.Target && cell.Form == CorpusWasmForm.Optimized)))
                {
                    var optimizedExecution = execution with
                    {
                        Observations = execution.OptimizedObservations,
                        ModulePath = execution.OptimizedModulePath ?? execution.ModulePath,
                        ModuleSha256 = execution.OptimizedModuleSha256 ?? execution.ModuleSha256,
                        Backend = CorpusExecutionBackend.Simulated,
                        Form = CorpusWasmForm.Optimized,
                    };
                    VerifyObservation(input,
                        cells.IsDefault ? execution.Observations[input] : expected[input],
                        optimized, optimizedExecution,
                        cells.IsDefault ? "optimized Wasm changed raw Wasm: " : "optimized Wasm differs from desktop: ");
                }
            }
            foreach (var linkedExecution in linkedResults)
            {
                var execution = new NetWasmExecution(
                    linkedExecution.Observations,
                    DiagnosticTracePath: null,
                    linkedExecution.ModulePath,
                    linkedExecution.ModuleSha256,
                    linkedExecution.Target,
                    Executed: true)
                {
                    Backend = CorpusExecutionBackend.Linked,
                    Form = linkedExecution.Form,
                };
                VerifyObservation(input, expected[input],
                    linkedExecution.Observations[input], execution,
                    "linked Wasm differs from desktop: ");
            }
        }

        if (mismatches.Count != 0)
        {
            throw new AggregateException(
                $"Corpus comparison completed: {compared} observations, {mismatches.Count} mismatches.", mismatches);
        }

        void VerifyObservation(
            int input,
            OracleObservation reference,
            OracleObservation actual,
            NetWasmExecution execution,
            string prefix)
        {
            compared++;
            var comparison = comparer.Compare(reference, actual);
            if (comparison.Equivalent)
            {
                return;
            }
            var artifact = failures.Write(
                compilation, input, reference, actual, execution, prefix + comparison.Message);
            var mismatch = new CorpusOracleMismatchException(input,
                $"{compilation.Fixture.Name} {compilation.Profile} {execution.Target} input {input}: " +
                $"{prefix}{comparison.Message}; reproduction: {artifact}");
            if (!compilation.Fixture.ReportAllMismatches)
            {
                throw mismatch;
            }
            mismatches.Add(mismatch);
        }
    }
}
