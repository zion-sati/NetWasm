using System.Globalization;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IDesktopOracleProgressReporter
{
    void Report(string fixtureId, int completed, int total);

    void ReportBatchAttempt(
        string fixtureId,
        int attempt,
        int observed,
        int total,
        int batchSize);
}

internal sealed class DesktopOracleProgressReporter(
    RitProgressCounterStore counters) : IDesktopOracleProgressReporter
{
    private readonly RitProgressSnapshotWriter? _testWriter;

    internal DesktopOracleProgressReporter(TextWriter output)
        : this(new RitProgressCounterStore(), output)
    {
    }

    private DesktopOracleProgressReporter(RitProgressCounterStore counters, TextWriter output)
        : this(counters)
    {
        _testWriter = new RitProgressSnapshotWriter(counters, output);
    }

    public void Report(string fixtureId, int completed, int total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(completed, total);

        counters.Update(
            $"desktop:{fixtureId}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"RIT desktop-progress fixture={fixtureId} completed={completed}/{total}"));
        _testWriter?.WritePending();
    }

    public void ReportBatchAttempt(
        string fixtureId,
        int attempt,
        int observed,
        int total,
        int batchSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(observed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(observed, total);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, total);

        counters.Update(
            $"desktop-batch:{fixtureId}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"RIT desktop-batch-progress fixture={fixtureId} attempt={attempt} observed={observed}/{total} batch={batchSize}"));
        _testWriter?.WritePending();
    }
}
