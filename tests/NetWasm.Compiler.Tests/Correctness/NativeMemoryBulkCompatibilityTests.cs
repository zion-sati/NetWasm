namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NativeMemoryBulkCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void BulkOperationsAndZeroSizeAllocationsMatchDesktop() =>
        Run(new(
            "NativeMemoryBulkCompatibility",
            "NativeMemoryBulkFixture",
            NativeMemoryCompilationTests.BulkSource,
            [1])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = 42,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The fixture compares NativeMemory bulk and zero-size contracts against the desktop runtime.",
        });
}
