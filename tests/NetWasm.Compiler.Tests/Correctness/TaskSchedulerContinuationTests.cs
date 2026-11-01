namespace NetWasm.Compiler.Tests.Correctness;

public sealed class TaskSchedulerContinuationTests(CorrectnessTestRunner runner)
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
    public void ContinuationsPreserveSchedulerIdentityOptionsAndCancellation(string cell)
    {
        using var stream = typeof(TaskSchedulerContinuationTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/TaskSchedulerContinuations/Program.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("TaskSchedulerContinuations", "TaskSchedulerContinuationContracts", reader.ReadToEnd(), [42])
        {
            ExpectedReturnValue = 42,
            RequiresReactor = true,
            WasmEntryMethod = "RunReactor",
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization | OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });
    }
}
