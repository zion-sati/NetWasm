using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilInteractionProgramSlice(
    int Index,
    int Offset,
    RandomCilInteractionShard Shard);

internal static class RandomCilInteractionProgramSlicer
{
    public static ImmutableArray<RandomCilInteractionProgramSlice> Create(
        RandomCilInteractionShard shard,
        int maximumCasesPerProgram)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCasesPerProgram, 1);

        var sliceCount = checked(
            (shard.Cases.Length + maximumCasesPerProgram - 1) /
            maximumCasesPerProgram);
        var slices = ImmutableArray.CreateBuilder<RandomCilInteractionProgramSlice>(
            sliceCount);

        for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            var offset = checked(sliceIndex * maximumCasesPerProgram);
            var count = Math.Min(
                maximumCasesPerProgram,
                shard.Cases.Length - offset);
            var cases = shard.Cases
                .AsSpan(offset, count)
                .ToArray()
                .ToImmutableArray();
            slices.Add(
                new RandomCilInteractionProgramSlice(
                    sliceIndex,
                    offset,
                    new RandomCilInteractionShard(shard.Index, cases)));
        }

        return slices.MoveToImmutable();
    }
}
