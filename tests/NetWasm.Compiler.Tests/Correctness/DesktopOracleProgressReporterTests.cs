// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DesktopOracleProgressReporterTests
{
    [Fact]
    public void WritesExactInputProgressWithoutInputValues()
    {
        using var output = new StringWriter();
        var reporter = new DesktopOracleProgressReporter(output);

        reporter.Report("fixture", completed: 7, total: 20);

        Assert.Contains("completed=7/20", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WritesExactBatchAttemptWithoutInputValues()
    {
        using var output = new StringWriter();
        var reporter = new DesktopOracleProgressReporter(output);

        reporter.ReportBatchAttempt(
            "fixture",
            attempt: 3,
            observed: 196,
            total: 700,
            batchSize: 4);

        var status = output.ToString();
        Assert.Contains("attempt=3", status, StringComparison.Ordinal);
        Assert.Contains("observed=196/700", status, StringComparison.Ordinal);
        Assert.Contains("batch=4", status, StringComparison.Ordinal);
    }
}
