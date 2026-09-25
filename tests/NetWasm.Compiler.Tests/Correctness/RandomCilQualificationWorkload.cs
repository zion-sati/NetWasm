namespace NetWasm.Compiler.Tests.Correctness;

public static class RandomCilQualificationWorkload
{
    public const int ExhaustiveProgramCount = 414_720;
    public const int ProgramsPerShard = 200;
    public const int ExhaustiveShardCount =
        (ExhaustiveProgramCount + ProgramsPerShard - 1) / ProgramsPerShard;

    public static int GetCaseCount(int? shardCount)
    {
        var selectedShardCount = shardCount ?? ExhaustiveShardCount;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selectedShardCount);
        return selectedShardCount;
    }
}
