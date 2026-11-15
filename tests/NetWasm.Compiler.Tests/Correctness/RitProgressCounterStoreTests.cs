// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RitProgressCounterStoreTests
{
    [Fact]
    public void WorkerUpdatesRemainBufferedUntilTheSingleWriterSnapshotsThem()
    {
        var counters = new RitProgressCounterStore();
        using var output = new StringWriter();
        var writer = new RitProgressSnapshotWriter(counters, output);

        Parallel.For(0, 100, completed =>
            counters.Update("desktop", $"completed={completed}/100"));

        Assert.Equal(string.Empty, output.ToString());
        writer.WritePending();
        var firstSnapshot = output.ToString();
        Assert.Contains("/100", firstSnapshot, StringComparison.Ordinal);

        writer.WritePending();
        Assert.Equal(firstSnapshot, output.ToString());
    }

    [Fact]
    public void SnapshotContainsOnlyTheLatestExactCounterForEachWorker()
    {
        var counters = new RitProgressCounterStore();
        counters.Update("a", "completed=1/10");
        counters.Update("a", "completed=7/10");
        counters.Update("b", "completed=3/10");

        var snapshot = counters.SnapshotAfter(0);

        Assert.Equal(2, snapshot.Length);
        Assert.Contains(snapshot, counter => counter.Line == "completed=7/10");
        Assert.Contains(snapshot, counter => counter.Line == "completed=3/10");
    }

    [Fact]
    public void ProductionReporterWorkersNeverWriteAndHeartbeatOwnsStdout()
    {
        var counters = new RitProgressCounterStore();
        using var output = new StringWriter();
        var writer = new RitProgressSnapshotWriter(counters, output);
        var desktop = new DesktopOracleProgressReporter(counters);
        var campaign = new ConsoleRandomCilCampaignProgressReporter(counters, writer);

        Parallel.For(0, 100, completed => desktop.Report("fixture", completed, 100));
        desktop.Report("fixture", 100, 100);
        desktop.ReportBatchAttempt(
            "fixture",
            attempt: 3,
            observed: 96,
            total: 100,
            batchSize: 4);

        Assert.Equal(string.Empty, output.ToString());
        campaign.Heartbeat(["case"], 0, 1, TimeSpan.FromSeconds(1));

        var snapshot = output.ToString();
        Assert.Contains("completed=100/100", snapshot, StringComparison.Ordinal);
        Assert.Contains("attempt=3", snapshot, StringComparison.Ordinal);
        Assert.Contains("observed=96/100", snapshot, StringComparison.Ordinal);
        Assert.Contains("batch=4", snapshot, StringComparison.Ordinal);
        Assert.Contains("RIT heartbeat", snapshot, StringComparison.Ordinal);
    }
}
