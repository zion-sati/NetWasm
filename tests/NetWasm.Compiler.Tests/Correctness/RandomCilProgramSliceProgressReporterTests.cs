// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilProgramSliceProgressReporterTests
{
    [Fact]
    public void ReportsExactCompletedAndTotalProgramSlices()
    {
        var counters = new RitProgressCounterStore();
        using var output = new StringWriter(
            System.Globalization.CultureInfo.InvariantCulture);
        var reporter = new RandomCilCaseProgressReporter(counters);
        var writer = new RitProgressSnapshotWriter(counters, output);

        reporter.ReportProgramSlices("rit-shard-0080-Debug", 7, 14);
        writer.WritePending();

        Assert.Contains(
            "RIT case-slice-progress fixture=rit-shard-0080-Debug completed=7/14",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 14)]
    [InlineData(15, 14)]
    [InlineData(0, 0)]
    public void RejectsInvalidSliceCounters(int completed, int total)
    {
        var reporter = new RandomCilCaseProgressReporter(
            new RitProgressCounterStore());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reporter.ReportProgramSlices(
                "rit-shard-0080-Debug",
                completed,
                total));
    }
}
