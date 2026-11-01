namespace NetWasm.Compiler.Tests.Correctness;

public sealed class TaskSchedulerEventTests(CorrectnessTestRunner runner)
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
    public void UnobservedEventsPreserveSenderAndExceptionLifetimes(string cell)
    {
        using var stream = typeof(TaskSchedulerEventTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/TaskSchedulerEvents/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("TaskSchedulerEvents", "TaskSchedulerEventContracts", reader.ReadToEnd(), [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });
    }
}
