using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilInteractionShard(
    int Index,
    ImmutableArray<RandomCilInteractionCase> Cases);

internal interface IRandomCilInteractionShardPlanner
{
    int CasesPerShard { get; }

    ImmutableArray<RandomCilInteractionShard> CreateShards();
}

internal sealed class RandomCilInteractionShardPlanner(
    IRandomCilInteractionMatrix matrix) : IRandomCilInteractionShardPlanner
{
    public const int DefaultCasesPerShard =
        RandomCilQualificationWorkload.ProgramsPerShard;
    public const int ExhaustiveShardCount =
        RandomCilQualificationWorkload.ExhaustiveShardCount;

    public int CasesPerShard => DefaultCasesPerShard;

    public ImmutableArray<RandomCilInteractionShard> CreateShards()
    {
        var cases = matrix.GenerateExhaustive();
        var shards = ImmutableArray.CreateBuilder<RandomCilInteractionShard>(
            (cases.Length + CasesPerShard - 1) / CasesPerShard);

        for (var offset = 0; offset < cases.Length; offset += CasesPerShard)
        {
            var length = Math.Min(CasesPerShard, cases.Length - offset);
            shards.Add(new(
                shards.Count,
                cases.AsSpan(offset, length).ToArray().ToImmutableArray()));
        }

        return shards.ToImmutable();
    }
}
