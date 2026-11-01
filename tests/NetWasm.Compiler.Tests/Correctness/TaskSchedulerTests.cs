namespace NetWasm.Compiler.Tests.Correctness;

public sealed class TaskSchedulerTests(CorrectnessTestRunner runner)
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
    public void SchedulerSelectionAndExecutionMatchDesktop(string cell)
    {
        using var stream = typeof(TaskSchedulerTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/TaskScheduler/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("TaskScheduler", "TaskSchedulerContracts", reader.ReadToEnd(), [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });
    }
}
