namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ThreadLocalTests(CorrectnessTestRunner runner)
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
    public void ThreadLocalPreservesDesktopLifecycleAndFactorySemantics(string cell)
    {
        using var stream = typeof(ThreadLocalTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/ThreadLocal/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("ThreadLocal", "ThreadLocalContracts", reader.ReadToEnd(), [42])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });
    }
}
