using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IOracleOperationProgressReporter
{
    void Report(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        OracleOperationPlan operationPlan);

    void ReportExecutionInputs(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        int completed,
        int total);

    void ReportCompilerPhases(
        string fixtureId,
        WasmTarget target,
        int completed,
        int total);

    void ReportBatchAttempt(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        int attempt,
        int observed,
        int total,
        int batchSize);
}

internal readonly record struct OracleOperationPlan(
    bool ExecuteDirect,
    bool ExecuteOptimized)
{
    public static OracleOperationPlan Create(
        WasmTarget target,
        CorpusFixture fixture)
    {
        var executeDirect = target != WasmTarget.Wasm64 || fixture.ExecuteWasm64;
        return new(executeDirect, executeDirect && fixture.ExecuteOptimizedWasm);
    }
}

internal enum OracleOperationStage
{
    Compile,
    ValidateDirect,
    ExecuteDirect,
    Optimize,
    ValidateOptimized,
    ExecuteOptimized,
    Complete,
}

internal interface IOracleOperationProgressPlan
{
    (int Completed, int Total) GetProgress(
        OracleOperationPlan operationPlan,
        OracleOperationStage stage);
}

internal sealed class OracleOperationProgressPlan : IOracleOperationProgressPlan
{
    private static readonly OracleOperationStage[] OptimizedStages =
    [
        OracleOperationStage.Compile,
        OracleOperationStage.ValidateDirect,
        OracleOperationStage.ExecuteDirect,
        OracleOperationStage.Optimize,
        OracleOperationStage.ValidateOptimized,
        OracleOperationStage.ExecuteOptimized,
    ];

    private static readonly OracleOperationStage[] ValidationOnlyStages =
    [
        OracleOperationStage.Compile,
        OracleOperationStage.ValidateDirect,
    ];

    private static readonly OracleOperationStage[] DirectStages =
    [
        OracleOperationStage.Compile,
        OracleOperationStage.ValidateDirect,
        OracleOperationStage.ExecuteDirect,
    ];

    public (int Completed, int Total) GetProgress(
        OracleOperationPlan operationPlan,
        OracleOperationStage stage)
    {
        var stages = operationPlan switch
        {
            { ExecuteDirect: false } => ValidationOnlyStages,
            { ExecuteOptimized: false } => DirectStages,
            _ => OptimizedStages,
        };
        if (stage == OracleOperationStage.Complete)
        {
            return (stages.Length, stages.Length);
        }

        var index = Array.IndexOf(stages, stage);
        if (index < 0)
        {
            throw new ArgumentException(
                $"Stage {stage} is not applicable to the selected oracle operation plan.",
                nameof(stage));
        }

        return (index, stages.Length);
    }
}

internal sealed class OracleOperationProgressReporter : IOracleOperationProgressReporter
{
    private readonly IOracleOperationProgressPlan _plan;
    private readonly RitProgressCounterStore _counters;
    private readonly RitProgressSnapshotWriter? _testWriter;

    public OracleOperationProgressReporter(IOracleOperationProgressPlan plan)
        : this(plan, new RitProgressCounterStore())
    {
    }

    internal OracleOperationProgressReporter(IOracleOperationProgressPlan plan, TextWriter output)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _counters = new RitProgressCounterStore();
        _testWriter = new RitProgressSnapshotWriter(_counters, output);
    }

    public OracleOperationProgressReporter(
        IOracleOperationProgressPlan plan,
        RitProgressCounterStore counters)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    }

    public void Report(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        OracleOperationPlan operationPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        var (completed, total) = _plan.GetProgress(operationPlan, stage);
        var stageName = stage.ToString().ToLowerInvariant();

        var message = $"RIT oracle-progress fixture={fixtureId} target={target} stage={stageName} completed={completed}/{total} started={DateTimeOffset.UtcNow:O}";
        _counters.Update($"oracle:{fixtureId}:{target}", message);
        _testWriter?.WritePending();
    }

    public void ReportExecutionInputs(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        int completed,
        int total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        if (stage is not (OracleOperationStage.ExecuteDirect or
            OracleOperationStage.ExecuteOptimized))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if (completed < 0 || completed > total || total <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        var stageName = stage.ToString().ToLowerInvariant();
        var message = $"RIT oracle-input-progress fixture={fixtureId} target={target} stage={stageName} completed={completed}/{total}";
        _counters.Update($"oracle:{fixtureId}:{target}", message);
        _testWriter?.WritePending();
    }

    public void ReportCompilerPhases(
        string fixtureId,
        WasmTarget target,
        int completed,
        int total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        if (completed < 0 || completed > total || total <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        var message = $"RIT compiler-progress fixture={fixtureId} target={target} completed={completed}/{total}";
        _counters.Update($"oracle:{fixtureId}:{target}", message);
        _testWriter?.WritePending();
    }

    public void ReportBatchAttempt(
        string fixtureId,
        WasmTarget target,
        OracleOperationStage stage,
        int attempt,
        int observed,
        int total,
        int batchSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        if (stage is not (OracleOperationStage.ExecuteDirect or
            OracleOperationStage.ExecuteOptimized))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(observed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(observed, total);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, total);

        var stageName = stage.ToString().ToLowerInvariant();
        var message = $"RIT oracle-batch-progress fixture={fixtureId} target={target} stage={stageName} attempt={attempt} observed={observed}/{total} batch={batchSize}";
        _counters.Update($"oracle-batch:{fixtureId}:{target}:{stageName}", message);
        _testWriter?.WritePending();
    }
}
