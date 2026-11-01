namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MonitorTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Debug-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    [InlineData("Debug-Wasm32-Optimized-Linked")]
    [InlineData("Debug-Wasm64-Optimized-Linked")]
    [InlineData("Release-Wasm32-Optimized-Linked")]
    [InlineData("Release-Wasm64-Optimized-Linked")]
    public void MonitorMatchesDesktopSingleThreadSemanticsAndReleasesTargets(string cell)
    {
        using var stream = typeof(MonitorTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/Monitor/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("Monitor", "MonitorContracts", reader.ReadToEnd(), [42])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });
    }
}
