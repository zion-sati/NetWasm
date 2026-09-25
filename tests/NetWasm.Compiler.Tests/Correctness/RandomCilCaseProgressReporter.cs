using System.Globalization;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IRandomCilCaseProgressReporter
{
    void Report(string fixtureId, RandomCilCaseStage stage);

    void ReportProgramSlices(string fixtureId, int completed, int total)
    {
    }
}

internal enum RandomCilCaseStage
{
    PrepareTemplate,
    PatchShard,
    DesktopOracle,
    NetWasmOracles,
    Complete,
}

internal sealed class RandomCilCaseProgressReporter(
    RitProgressCounterStore counters) : IRandomCilCaseProgressReporter
{
    private const int Total = 4;
    private readonly RitProgressSnapshotWriter? _testWriter;

    internal RandomCilCaseProgressReporter(TextWriter output)
        : this(new RitProgressCounterStore(), output)
    {
    }

    private RandomCilCaseProgressReporter(RitProgressCounterStore counters, TextWriter output)
        : this(counters)
    {
        _testWriter = new RitProgressSnapshotWriter(counters, output);
    }

    public void Report(string fixtureId, RandomCilCaseStage stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        var completed = stage == RandomCilCaseStage.Complete ? Total : (int)stage;
        counters.Update(
            $"case:{fixtureId}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"RIT case-progress fixture={fixtureId} stage={stage.ToString().ToLowerInvariant()} completed={completed}/{Total}"));
        _testWriter?.WritePending();
    }

    public void ReportProgramSlices(string fixtureId, int completed, int total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(completed, total);

        counters.Update(
            $"case-slices:{fixtureId}",
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"RIT case-slice-progress fixture={fixtureId} completed={completed}/{total}"));
    }
}
