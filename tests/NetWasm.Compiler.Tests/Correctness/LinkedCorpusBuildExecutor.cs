using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusBuildStepResult(LinkedCorpusBuildStep Step, QualifiedProcessResult Result);

internal sealed class LinkedCorpusBuildException(
    LinkedCorpusBuildStage stage,
    ImmutableArray<LinkedCorpusBuildStepResult> completed) :
    Exception($"linked corpus build failed at {stage}", completed[^1].Result.LaunchException)
{
    public LinkedCorpusBuildStage Stage { get; } = stage;
    public ImmutableArray<LinkedCorpusBuildStepResult> Completed { get; } = completed;
}

internal interface ILinkedCorpusBuildExecutor
{
    ImmutableArray<LinkedCorpusBuildStepResult> Execute(
        LinkedCorpusBuildPlan plan, CancellationToken cancellationToken = default);
}

internal sealed class LinkedCorpusBuildExecutor(IQualifiedProcessRunner processes) : ILinkedCorpusBuildExecutor
{
    private readonly IQualifiedProcessRunner _processes =
        processes ?? throw new ArgumentNullException(nameof(processes));

    public ImmutableArray<LinkedCorpusBuildStepResult> Execute(
        LinkedCorpusBuildPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Steps.IsDefaultOrEmpty)
            throw new ArgumentException("linked build plan must contain stages", nameof(plan));
        var completed = ImmutableArray.CreateBuilder<LinkedCorpusBuildStepResult>();
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _processes.Run(step.Process, cancellationToken);
            completed.Add(new(step, result));
            if (!result.Succeeded)
                throw new LinkedCorpusBuildException(step.Stage, completed.ToImmutable());
        }
        return completed.ToImmutable();
    }
}
