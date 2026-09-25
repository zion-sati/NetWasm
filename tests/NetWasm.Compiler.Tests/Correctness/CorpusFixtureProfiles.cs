namespace NetWasm.Compiler.Tests.Correctness;

internal static class CorpusFixtureProfiles
{
    internal static CorpusFixture WithFullExecutionMatrix(this CorpusFixture fixture) =>
        fixture with
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        };
}
