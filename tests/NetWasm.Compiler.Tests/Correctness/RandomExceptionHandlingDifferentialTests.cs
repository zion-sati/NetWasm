namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomExceptionHandlingDifferentialTests(
    CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(101)]
    [InlineData(509)]
    public void DeterministicGeneratedExceptionRegionsMatchCoreClr(int seed) =>
        runner.Run(RandomExceptionHandlingFixtureFactory.Create(seed));

    [Fact]
    public void SameSeedProducesIdenticalSourceAndFrozenPilotCoverage()
    {
        var first = RandomExceptionHandlingFixtureFactory.Create(101);
        var second = RandomExceptionHandlingFixtureFactory.Create(101);

        Assert.Equal(first.Source, second.Source);
        Assert.Contains("catch (System.ArgumentException) when", first.Source);
        Assert.Contains("finally", first.Source);
        Assert.Contains("throw;", first.Source);
        Assert.Contains("_root!.Value", first.Source);
        Assert.Equal<int>([101, 509], RandomExceptionHandlingFixtureFactory.PilotSeeds);
    }
}
