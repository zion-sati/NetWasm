// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleOperationProgressReporterTests
{
    [Fact]
    public void PersistsExactTargetOperationProgress()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(new OracleOperationProgressPlan(), output);

        reporter.Report(
            "fixture",
            WasmTarget.Wasm32,
            OracleOperationStage.ExecuteDirect,
            new OracleOperationPlan(ExecuteDirect: true, ExecuteOptimized: true));

        var status = output.ToString();
        Assert.Contains("target=Wasm32", status, StringComparison.Ordinal);
        Assert.Contains("stage=executedirect", status, StringComparison.Ordinal);
        Assert.Contains("completed=2/6", status, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutableWasm64UsesTheFullSelectedOperationPlan()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(new OracleOperationProgressPlan(), output);

        reporter.Report(
            "fixture",
            WasmTarget.Wasm64,
            OracleOperationStage.ExecuteDirect,
            new OracleOperationPlan(ExecuteDirect: true, ExecuteOptimized: true));

        Assert.Contains("completed=2/6", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationOnlyTargetCompletesItsExactTwoOperationPlan()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(new OracleOperationProgressPlan(), output);

        reporter.Report(
            "fixture",
            WasmTarget.Wasm64,
            OracleOperationStage.Complete,
            new OracleOperationPlan(ExecuteDirect: false, ExecuteOptimized: false));

        Assert.Contains("completed=2/2", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsExactExecutionInputProgressWithoutWorkerStdout()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(
            new OracleOperationProgressPlan(),
            output);

        reporter.ReportExecutionInputs(
            "fixture",
            WasmTarget.Wasm64,
            OracleOperationStage.ExecuteOptimized,
            completed: 100,
            total: 1400);

        var status = output.ToString();
        Assert.Contains("target=Wasm64", status, StringComparison.Ordinal);
        Assert.Contains("stage=executeoptimized", status, StringComparison.Ordinal);
        Assert.Contains("completed=100/1400", status, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsExactCompilerPhaseProgressWithoutDiagnostics()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(
            new OracleOperationProgressPlan(),
            output);

        reporter.ReportCompilerPhases(
            "fixture",
            WasmTarget.Wasm32,
            completed: 3,
            total: 10);

        var status = output.ToString();
        Assert.Contains("target=Wasm32", status, StringComparison.Ordinal);
        Assert.Contains("completed=3/10", status, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsExactBatchAttemptWithoutInputValues()
    {
        using var output = new StringWriter();
        var reporter = new OracleOperationProgressReporter(
            new OracleOperationProgressPlan(),
            output);

        reporter.ReportBatchAttempt(
            "fixture",
            WasmTarget.Wasm32,
            OracleOperationStage.ExecuteDirect,
            attempt: 3,
            observed: 196,
            total: 700,
            batchSize: 4);

        var status = output.ToString();
        Assert.Contains("target=Wasm32", status, StringComparison.Ordinal);
        Assert.Contains("stage=executedirect", status, StringComparison.Ordinal);
        Assert.Contains("attempt=3", status, StringComparison.Ordinal);
        Assert.Contains("observed=196/700", status, StringComparison.Ordinal);
        Assert.Contains("batch=4", status, StringComparison.Ordinal);
    }
}
