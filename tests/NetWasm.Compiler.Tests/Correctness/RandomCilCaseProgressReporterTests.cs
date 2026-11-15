// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilCaseProgressReporterTests
{
    [Fact]
    public void WritesExactPreparationAndOracleProgressToTheConfiguredOutput()
    {
        using var output = new StringWriter();
        var reporter = new RandomCilCaseProgressReporter(output);

        reporter.Report("fixture", RandomCilCaseStage.DesktopOracle);
        reporter.Report("fixture", RandomCilCaseStage.Complete);

        var status = output.ToString();
        Assert.Contains("stage=desktoporacle completed=2/4", status, StringComparison.Ordinal);
        Assert.Contains("stage=complete completed=4/4", status, StringComparison.Ordinal);
    }
}
